// ============================================================================
// File: Services/OnlineTagIngestionService.cs
//
// PURPOSE: Bridges online R700 webhook data into your EXISTING offline pipeline.
//
// ARCHITECTURE INSIGHT:
//   Your existing offline flow is:
//     UploadRFIDFileAutoAsync → UploadRFIDFileEventLevelAsync
//       → ParseSqliteFileAsync → RawRFIDReading rows + UploadBatch
//       → ProcessCompleteWorkflowAsync (Phase 1 → 1.5 → 2 → 2.5 → 3)
//
//   The online flow REUSES everything after ParseSqliteFileAsync:
//     R700 Webhook POST → THIS SERVICE
//       → Creates RawRFIDReading rows + UploadBatch (same tables, same schema)
//       → Same ProcessCompleteWorkflowAsync runs on these rows
//
//   The key insight is: your entire pipeline operates on RawRFIDReading rows
//   grouped by UploadBatch. It doesn't care whether those rows came from a
//   .db file or a webhook. So we just need to INSERT the right rows.
//
// WHAT THIS SERVICE DOES:
//   1. Receives parsed webhook events from the controller
//   2. Resolves Device by hostname/MAC → Device.DeviceId (your existing field)
//   3. Creates/reuses an UploadBatch with SourceType = "online_webhook"
//   4. Inserts RawRFIDReading rows (same schema as ParseSqliteFileAsync output)
//   5. Resolves EPC → Participant → Checkpoint for SignalR live display
//   6. Pushes live crossing events to React via SignalR
//
// WHAT THIS SERVICE DOES NOT DO:
//   - It does NOT run deduplication, normalization, or split time calculation.
//   - Those remain in your existing ProcessCompleteWorkflowAsync.
//   - You can trigger that workflow manually ("Calculate Results" button),
//     on a timer, or automatically after each webhook batch.
// ============================================================================

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Hubs;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services;

public class OnlineTagIngestionService
{
    private readonly IUnitOfWork<RaceSyncDbContext> _repository;
    private readonly IUserContextService _userContext;
    private readonly IHubContext<RaceHub> _raceHub;
    private readonly IRaceNotificationService _raceNotificationService;
    private readonly ILogger<OnlineTagIngestionService> _logger;

    /// <summary>
    /// Dedup window matching your existing DEFAULT_DEDUP_WINDOW_SECONDS = 30.
    /// Readings from the same EPC within this window are the same "pass".
    /// </summary>
    private const double DEDUP_WINDOW_SECONDS = 30.0;

    public OnlineTagIngestionService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IUserContextService userContext,
        IHubContext<RaceHub> raceHub,
        IRaceNotificationService raceNotificationService,
        ILogger<OnlineTagIngestionService> logger)
    {
        _repository = repository;
        _userContext = userContext;
        _raceHub = raceHub;
        _raceNotificationService = raceNotificationService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // MAIN ENTRY POINT — Called by RfidWebhookController
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Processes a batch of tag events from an R700 webhook POST.
    ///
    /// This method:
    ///   1. Resolves the reader hostname/MAC to your existing Device record
    ///   2. Finds the Checkpoint → Event association (same as UploadRFIDFileAutoAsync)
    ///   3. Creates RawRFIDReading rows in the SAME table your offline flow uses
    ///   4. Pushes live crossing events to React via SignalR
    ///
    /// After this, your existing ProcessCompleteWorkflowAsync can process these
    /// readings through the full pipeline (Phase 1 → 1.5 → 2 → 2.5 → 3).
    /// </summary>
    public async Task ProcessWebhookPayload(R700WebhookPayload payload)
    {
        if (payload.TagInventoryEvents == null || !payload.TagInventoryEvents.Any())
            return;

        var tenantId = _userContext.TenantId;

        // ── Step 1: Resolve the reader device ──
        // Same logic as UploadRFIDFileAutoAsync: find Device by DeviceId (MAC)
        var device = await ResolveDevice(payload.Hostname, payload.MacAddress, tenantId);

        if (device == null)
        {
            _logger.LogWarning(
                "Webhook from unknown reader: Host={Host}, MAC={Mac}. " +
                "Register the device first via the admin UI.",
                payload.Hostname, payload.MacAddress);
            return;
        }

        // ── Step 2: Find the checkpoint → event/race context ──
        // Same as UploadRFIDFileAutoAsync lines 882-897:
        //   checkpoint = checkpoints where DeviceId == device.Id
        var checkpointRepo = _repository.GetRepository<Checkpoint>();
        var checkpoint = await checkpointRepo.GetQuery(cp =>
                cp.DeviceId == device.Id &&
                cp.AuditProperties.IsActive &&
                !cp.AuditProperties.IsDeleted)
            .OrderByDescending(cp => cp.AuditProperties.CreatedDate)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (checkpoint == null)
        {
            _logger.LogWarning(
                "No checkpoint assigned to device '{DeviceName}' (ID: {DeviceId}). " +
                "Configure the device checkpoint assignment first.",
                device.Name, device.Id);
            return;
        }

        var eventId = checkpoint.EventId;

        // ── Step 2b: Look up event timezone for local time conversion ──
        // Your offline flow (ParseSqliteFileAsync) converts timestamps using the
        // user-provided TimeZoneId. Online mode gets the timezone from the Event table.
        // This ensures ReadTimeLocal matches what the offline flow would produce.
        var eventRepo = _repository.GetRepository<Event>();
        var eventEntity = await eventRepo.GetQuery(e => e.Id == eventId)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        var eventTimeZoneId = eventEntity?.TimeZone ?? "UTC";
        TimeZoneInfo eventTimeZone;
        try
        {
            eventTimeZone = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning(
                "Event {EventId} has unknown timezone '{TimeZone}', falling back to UTC",
                eventId, eventTimeZoneId);
            eventTimeZone = TimeZoneInfo.Utc;
            eventTimeZoneId = "UTC";
        }

        // ── Step 3: Get or create the UploadBatch for this device + session ──
        // We create one UploadBatch per device per day for online mode.
        // This groups all webhook readings from a device into one batch,
        // just like one .db file = one batch in offline mode.
        var batch = await GetOrCreateOnlineBatch(device, eventId, tenantId);

        // ── Step 4: Convert webhook events to RawRFIDReading rows ──
        // Same schema as ParseSqliteFileAsync output (lines 1709-1730)
        var readings = new List<RawRFIDReading>();
        var userId = _userContext.UserId;

        // CRITICAL: Use the MAC string (Device.DeviceId), NOT device.Id.ToString().
        // This must match what your offline flow stores — the MAC extracted from .db filename.
        // Phase 1.5 resolves devices by looking up this string in Device.DeviceId/Device.Name.
        var deviceIdForReadings = !string.IsNullOrEmpty(device.DeviceMacAddress)
            ? device.DeviceMacAddress
            : !string.IsNullOrEmpty(device.Name)
                ? device.Name
                : device.Id.ToString();

        foreach (var tagEvent in payload.TagInventoryEvents)
        {
            if (string.IsNullOrEmpty(tagEvent.Epc))
                continue;

            var reading = MapWebhookEventToRawReading(
                tagEvent, batch.Id, deviceIdForReadings,
                userId, eventTimeZone, eventTimeZoneId);

            if (reading != null)
                readings.Add(reading);
        }

        if (readings.Count == 0)
            return;

        // ── Step 5: Insert into RawRFIDReading table ──
        // Same table your offline flow writes to
        var readingRepo = _repository.GetRepository<RawRFIDReading>();
        foreach (var reading in readings)
        {
            await readingRepo.AddAsync(reading);
        }
        await _repository.SaveChangesAsync();

        // Update batch statistics (same as UploadRFIDFileEventLevelAsync lines 787-795)
        batch.TotalReadings += readings.Count;
        batch.UniqueEpcs = await readingRepo.GetQuery(r =>
                r.BatchId == batch.Id &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
            .Select(r => r.Epc)
            .Distinct()
            .CountAsync();

        batch.TimeRangeEnd = readings.Max(r => r.TimestampMs);
        if (batch.TimeRangeStart == 0)
            batch.TimeRangeStart = readings.Min(r => r.TimestampMs);

        var batchRepo = _repository.GetRepository<UploadBatch>();
        await batchRepo.UpdateAsync(batch);
        await _repository.SaveChangesAsync();

        _logger.LogDebug(
            "Webhook ingested: {Count} readings from {DeviceName} (DeviceId={DeviceId}, MAC={Mac}) into batch {BatchId}",
            readings.Count, device.Name, deviceIdForReadings, device.DeviceMacAddress, batch.Id);

        // ── Step 6: Push live crossing events to React ──
        // This is the ONLINE-ONLY addition — immediate visual feedback.
        // The full pipeline (dedup, normalize, split times, results) runs later.
        await PushLiveCrossingEvents(readings, device, checkpoint, eventId);

        // Update device heartbeat
        device.IsOnline = true;
        device.LastSeenAt = DateTime.UtcNow;
        var deviceRepo = _repository.GetRepository<Device>();
        await deviceRepo.UpdateAsync(device);
        await _repository.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // BATCH MANAGEMENT — One UploadBatch per device per day for online mode
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets an existing online batch for today, or creates a new one.
    /// This is analogous to the UploadBatch created in UploadRFIDFileEventLevelAsync
    /// (lines 729-753), but for online/streaming data.
    ///
    /// We use one batch per device per day so that:
    ///   - All webhook readings from a device group together (like one .db file)
    ///   - ProcessCompleteWorkflowAsync can find them by batch
    ///   - FileHash deduplication doesn't apply (no file to hash)
    /// </summary>
    private async Task<UploadBatch> GetOrCreateOnlineBatch(
        Device device, int eventId, int tenantId)
    {
        var batchRepo = _repository.GetRepository<UploadBatch>();
        var today = DateTime.UtcNow.Date;

        // CRITICAL: Must store the MAC string (Device.DeviceId), NOT device.Id.ToString().
        // Phase 1.5 (AssignCheckpointsForLoopRaceAsync) resolves devices via a lookup
        // built from Device.DeviceId (MAC) and Device.Name. If we store the integer ID
        // (e.g., "1016"), Phase 1.5 can't resolve it and skips ALL readings → 0 assignments
        // → 0 normalized readings → all DNF.
        //
        // Your offline flow stores the MAC extracted from the .db filename (e.g., "00162512dbb0").
        // We must do the same here.
        var deviceId = !string.IsNullOrEmpty(device.DeviceMacAddress)
            ? device.DeviceMacAddress
            : !string.IsNullOrEmpty(device.Name)
                ? device.Name
                : device.Id.ToString();

        // Look for an existing online batch for this device today
        var existingBatch = await batchRepo.GetQuery(b =>
                b.DeviceId == deviceId &&
                b.EventId == eventId &&
                b.SourceType == "online_webhook" &&
                b.ProcessingStartedAt != null &&
                b.ProcessingStartedAt.Value.Date == today &&
                b.AuditProperties.IsActive &&
                !b.AuditProperties.IsDeleted)
            .FirstOrDefaultAsync();

        if (existingBatch != null)
            return existingBatch;

        // Create new batch — mirrors UploadRFIDFileEventLevelAsync batch creation
        var batch = new UploadBatch
        {
            RaceId = null,          // Event-level, same as auto upload
            EventId = eventId,
            DeviceId = deviceId,    // MAC or device identifier
            ExpectedCheckpointId = null,  // Determined during processing
            OriginalFileName = $"online_{deviceId}_{today:yyyy-MM-dd}",
            StoredFilePath = null,  // No file — data comes from webhook
            FileSizeBytes = 0,
            FileHash = $"online_{deviceId}_{today:yyyyMMdd}",  // Unique per device per day
            FileFormat = "WEBHOOK",
            Status = "uploading",   // Will be updated to "uploaded" as data arrives
            SourceType = "online_webhook",  // ← KEY DIFFERENTIATOR from offline
            IsLiveSync = true,              // ← Marks this as real-time data
            ProcessingStartedAt = DateTime.UtcNow,
            AuditProperties = new Models.Data.Common.AuditProperties
            {
                CreatedBy = _userContext.UserId,
                CreatedDate = DateTime.UtcNow,
                IsActive = true,
                IsDeleted = false
            }
        };

        await batchRepo.AddAsync(batch);
        await _repository.SaveChangesAsync();

        _logger.LogInformation(
            "Created online UploadBatch {BatchId} for device {DeviceId}, event {EventId}",
            batch.Id, deviceId, eventId);

        return batch;
    }

    // ─────────────────────────────────────────────────────────────────────
    // RAW READING MAPPING — Mirrors ParseSqliteFileAsync output format
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps an R700 webhook tag event to a RawRFIDReading entity.
    /// Output matches EXACTLY what ParseSqliteFileAsync produces (lines 1709-1730),
    /// so the downstream pipeline treats it identically.
    ///
    /// Timezone conversion mirrors ParseSqliteFileAsync lines 1696-1707:
    ///   readTimeUtc  = the actual UTC time from the R700
    ///   readTimeLocal = converted to the event's timezone (same as offline .db flow)
    /// </summary>
    private RawRFIDReading? MapWebhookEventToRawReading(
        R700TagInventoryEvent evt, int batchId, string deviceId, int userId,
        TimeZoneInfo eventTimeZone, string timeZoneId)
    {
        if (string.IsNullOrEmpty(evt.Epc))
            return null;

        // Parse the ISO 8601 timestamp from the R700
        // R700 IoT interface sends UTC timestamps (unlike .db files which may be local)
        DateTime readTimeUtc;
        if (!string.IsNullOrEmpty(evt.FirstSeenTimestamp)
            && DateTime.TryParse(evt.FirstSeenTimestamp, out var parsed))
        {
            readTimeUtc = parsed.ToUniversalTime();
        }
        else
        {
            readTimeUtc = DateTime.UtcNow;
        }

        // Convert UTC to event local time — mirrors ParseSqliteFileAsync line 1697:
        //   var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        //   readTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(readTimeUtc, tz);
        var readTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(readTimeUtc, eventTimeZone);

        // Convert to millisecond timestamp — same format as your .db files
        var timestampMs = ((DateTimeOffset)readTimeUtc).ToUnixTimeMilliseconds();

        // RSSI: R700 IoT interface sends centidBm (e.g., -6600 = -66.00 dBm)
        decimal? rssiDbm = evt.PeakRssiCdbm.HasValue
            ? evt.PeakRssiCdbm.Value / 100.0m
            : null;

        return new RawRFIDReading
        {
            BatchId = batchId,
            DeviceId = deviceId,
            Epc = evt.Epc.Replace(" ", "").ToUpperInvariant(),
            TimestampMs = timestampMs,
            Antenna = evt.AntennaPort,
            RssiDbm = rssiDbm,
            Channel = evt.Channel,
            ReadTimeLocal = readTimeLocal,     // Converted to event timezone (e.g., Asia/Kolkata)
            ReadTimeUtc = readTimeUtc,         // Original UTC from R700
            TimeZoneId = timeZoneId,           // Event's timezone ID (e.g., "Asia/Kolkata")
            ProcessResult = "Pending",          // Must be "Pending" so Phase 1 validates RSSI and assigns checkpoints
            SourceType = "online_webhook",
            AuditProperties = new Models.Data.Common.AuditProperties
            {
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsActive = true,
                IsDeleted = false
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // LIVE SIGNALR — Immediate visual feedback (before full pipeline runs)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes live crossing events to React via SignalR.
    /// This provides immediate visual feedback on the dashboard while the
    /// full pipeline (dedup → normalize → split times → results) runs later.
    ///
    /// Note: These are RAW readings, not yet deduplicated. The dashboard should
    /// handle duplicate display (e.g., show "latest" per bib at each checkpoint).
    /// </summary>
    private async Task PushLiveCrossingEvents(
        List<RawRFIDReading> readings,
        Device device,
        Checkpoint checkpoint,
        int eventId)
    {
        try
        {
            // Resolve EPCs to participants for display
            var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
            var epcs = readings.Select(r => r.Epc).Distinct().ToList();

            var epcToParticipant = await chipAssignmentRepo.GetQuery(ca =>
                    ca.Event.Id == eventId &&
                    !ca.UnassignedAt.HasValue &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted &&
                    epcs.Contains(ca.Chip.EPC))
                .AsNoTracking()
                .Select(ca => new
                {
                    EPC = ca.Chip.EPC,
                    ca.ParticipantId,
                    ca.Participant.BibNumber,
                    FirstName = ca.Participant.FirstName ?? "",
                    LastName = ca.Participant.LastName ?? "",
                    ca.Participant.RaceId
                })
                .ToDictionaryAsync(x => x.EPC, x => x);

            var crossingEvents = new List<CheckpointCrossingEvent>();

            var participantsToNotify = new List<(int ParticipantId, int RaceId)>();

            foreach (var reading in readings)
            {
                if (!epcToParticipant.TryGetValue(reading.Epc, out var participant))
                    continue;

                crossingEvents.Add(new CheckpointCrossingEvent
                {
                    ParticipantName = $"{participant.FirstName} {participant.LastName}".Trim(),
                    BibNumber = participant.BibNumber ?? "",
                    CheckpointName = checkpoint.Name ?? $"Checkpoint {checkpoint.Id}",
                    Epc = reading.Epc,
                    Timestamp = reading.ReadTimeUtc,
                    Rssi = (double)(reading.RssiDbm ?? 0),
                    AntennaPort = reading.Antenna ?? 0,
                    RaceId = participant.RaceId,
                    CheckpointId = checkpoint.Id
                });

                participantsToNotify.Add((participant.ParticipantId, participant.RaceId));
            }

            if (crossingEvents.Any())
            {
                // Group by race and send to appropriate SignalR groups
                foreach (var raceGroup in crossingEvents.GroupBy(e => e.RaceId))
                {
                    await _raceHub.Clients.Group($"race-{raceGroup.Key}")
                        .SendAsync("CheckpointCrossings", raceGroup.ToList());
                }

                _logger.LogDebug(
                    "Pushed {Count} live crossings from {DeviceName}",
                    crossingEvents.Count, device.Name);

                // Fire-and-forget checkpoint notifications (dedup handled inside service)
                foreach (var (participantId, raceId) in participantsToNotify.Distinct())
                {
                    var pId = participantId;
                    var rId = raceId;
                    var cId = checkpoint.Id;
                    _ = Task.Run(() => _raceNotificationService.NotifyCheckpointCrossingAsync(pId, cId, rId));
                }
            }
        }
        catch (Exception ex)
        {
            // SignalR push failure is non-critical — data is already saved
            _logger.LogWarning(ex, "Failed to push live crossing events via SignalR");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // DEVICE RESOLUTION — Matches your existing lookup pattern
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an R700 reader to your existing Device record.
    /// Tries hostname first, then MAC address (DeviceId field).
    /// Same pattern as UploadRFIDFileAutoAsync lines 862-869.
    /// </summary>
    private async Task<Device?> ResolveDevice(
        string? hostname, string? macAddress, int tenantId)
    {
        var deviceRepo = _repository.GetRepository<Device>();

        // Normalize MAC: R700 sends "00:16:25:12:db:b0", your DB stores "00162512dbb0"
        var macNormalized = macAddress?
            .Replace(":", "").Replace("-", "").ToLowerInvariant();

        // Try by hostname first (most reliable during webhook)
        if (!string.IsNullOrEmpty(hostname))
        {
            var device = await deviceRepo.GetQuery(d =>
                    d.Hostname == hostname &&
                    d.TenantId == tenantId &&
                    d.AuditProperties.IsActive &&
                    !d.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();

            if (device != null) return device;
        }

        // Try by MAC address (your existing DeviceId field)
        if (!string.IsNullOrEmpty(macNormalized))
        {
            var device = await deviceRepo.GetQuery(d =>
                    d.DeviceMacAddress == macNormalized &&
                    d.TenantId == tenantId &&
                    d.AuditProperties.IsActive &&
                    !d.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();

            if (device != null) return device;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // WEBHOOK JSON PARSING — Handles multiple R700 firmware formats
    // ─────────────────────────────────────────────────────────────────────

    public R700WebhookPayload ParseWebhookJson(JsonElement json)
    {
        var payload = new R700WebhookPayload
        {
            TagInventoryEvents = new List<R700TagInventoryEvent>()
        };

        if (json.TryGetProperty("hostname", out var hostProp))
            payload.Hostname = hostProp.GetString();
        if (json.TryGetProperty("macAddress", out var macProp))
            payload.MacAddress = macProp.GetString();

        // Pattern 1: Array at top level
        if (json.TryGetProperty("tag_inventory_events", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var evt in arr.EnumerateArray())
            {
                var e = ParseSingleTagEvent(evt);
                if (e != null) payload.TagInventoryEvents.Add(e);
            }
        }

        // Pattern 2: Single event
        if (json.TryGetProperty("tag_inventory_event", out var single))
        {
            var e = ParseSingleTagEvent(single);
            if (e != null) payload.TagInventoryEvents.Add(e);
        }

        // Pattern 3: Nested under "events"
        if (json.TryGetProperty("events", out var evts)
            && evts.ValueKind == JsonValueKind.Array)
        {
            foreach (var wrapper in evts.EnumerateArray())
            {
                if (wrapper.TryGetProperty("tagInventoryEvent", out var inner))
                {
                    var e = ParseSingleTagEvent(inner);
                    if (e != null) payload.TagInventoryEvents.Add(e);
                }
            }
        }

        return payload;
    }

    private static R700TagInventoryEvent? ParseSingleTagEvent(JsonElement evt)
    {
        var e = new R700TagInventoryEvent();

        if (evt.TryGetProperty("epc", out var epc))
            e.Epc = epc.GetString();
        else if (evt.TryGetProperty("epcHex", out var epcHex))
            e.Epc = epcHex.GetString();

        if (string.IsNullOrEmpty(e.Epc)) return null;

        if (evt.TryGetProperty("firstSeenTimestamp", out var fst))
            e.FirstSeenTimestamp = fst.GetString();
        if (evt.TryGetProperty("lastSeenTimestamp", out var lst))
            e.LastSeenTimestamp = lst.GetString();
        if (evt.TryGetProperty("antennaPort", out var ant))
            e.AntennaPort = ant.GetInt32();
        if (evt.TryGetProperty("peakRssiCdbm", out var rssi))
            e.PeakRssiCdbm = rssi.GetInt32();
        if (evt.TryGetProperty("channel", out var ch))
            e.Channel = ch.GetInt32();

        return e;
    }
}