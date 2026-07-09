using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Hubs;
using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class LiveReadingService : SimpleServiceBase, ILiveReadingService
    {
        private readonly IUnitOfWork<RaceSyncDbContext> _unitOfWork;
        private readonly IEncryptionService _encryptionService;
        private readonly IRFIDImportService _rfidImportService;
        private readonly IHubContext<RaceHub> _raceHub;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LiveReadingService> _logger;

        public LiveReadingService(
            IUnitOfWork<RaceSyncDbContext> unitOfWork,
            IEncryptionService encryptionService,
            IRFIDImportService rfidImportService,
            IHubContext<RaceHub> raceHub,
            IServiceScopeFactory scopeFactory,
            ILogger<LiveReadingService> logger)
        {
            _unitOfWork = unitOfWork;
            _encryptionService = encryptionService;
            _rfidImportService = rfidImportService;
            _raceHub = raceHub;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<LiveReadingResponse?> IngestAsync(
            string? deviceMac,
            string? deviceName,
            LiveReadingsRequest request,
            CancellationToken ct)
        {
            // DIAGNOSTIC (2026-07-08): Warning-level so Azure codeless attach forwards every
            // resolution step to App Insights traces — the client's 400s originate in THIS
            // method (device resolution), not in model binding. Drop to Debug once settled.
            _logger.LogWarning(
                "live-readings: received deviceMac={DeviceMac} deviceName={DeviceName} " +
                "body.deviceId={BodyDeviceId} body.deviceName={BodyDeviceName} readings={ReadingCount}",
                deviceMac, deviceName, request.DeviceId, request.DeviceName, request.Readings?.Count ?? 0);

            // BLIND resolution (2026-07-07) — IDENTICAL to the offline import-auto upload:
            // the device resolves to the EVENT via its newest active checkpoint mapping,
            // through the ONE shared resolver. Identity arrives as query params
            // (?deviceMac=… tried first, ?deviceName=… fallback) OR in the body
            // (deviceId / deviceName — the shape the Pi firmware actually sends,
            // 2026-07-09); every identifier goes through the SAME resolver, so no
            // divergent matching exists. No event/race ids from the caller; NO race
            // resolved here — the batch is EVENT-level (RaceId NULL) and the race is
            // resolved per read downstream via EPC → ChipAssignment → Participant →
            // RaceId, exactly like an offline file. Tenant is null: the Pi authenticates
            // via X-Device-Key, not a JWT.
            var identifiers = new[] { deviceMac, deviceName, request.DeviceId, request.DeviceName }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (identifiers.Count == 0)
            {
                ErrorMessage = "Device identity is required: pass ?deviceMac= / ?deviceName= " +
                    "query parameters or deviceId / deviceName in the body.";
                _logger.LogWarning(
                    "live-readings: REJECTED (controller maps to 400) — {Error}", ErrorMessage);
                return null;
            }

            if (request.Readings == null || request.Readings.Count == 0)
                return new LiveReadingResponse();

            DeviceEventResolution? resolution = null;
            string? deviceFoundError = null;
            foreach (var identifier in identifiers)
            {
                var attempt = await _rfidImportService.ResolveDeviceToEventAsync(identifier, tenantId: null);
                if (attempt.Succeeded)
                {
                    resolution = attempt;
                    _logger.LogWarning(
                        "live-readings: resolved '{Identifier}' → Device.Id={DeviceDbId} Checkpoint={CheckpointId} Event={EventId} (race is resolved per read downstream)",
                        identifier, attempt.DeviceDbId, attempt.CheckpointId, attempt.EventId);
                    break;
                }
                _logger.LogWarning(
                    "live-readings: identifier '{Identifier}' did NOT resolve — DeviceFound={DeviceFound} Error={Error}",
                    identifier, attempt.DeviceFound, attempt.Error);
                // A matched-but-unconfigured device (no checkpoint mapping) is the more
                // specific failure — surface it over a generic not-found.
                if (attempt.DeviceFound)
                    deviceFoundError ??= attempt.Error;
            }

            if (resolution == null)
            {
                // Name EVERYTHING the Pi sent — the operator sees exactly what failed to
                // match a registered device.
                ErrorMessage = deviceFoundError ??
                    $"Device '{string.Join("' / '", identifiers)}' not found in the system. Please ensure the device is registered.";
                _logger.LogWarning(
                    "live-readings: REJECTED (controller maps to {Status}) — {Error}",
                    deviceFoundError != null ? "400, checkpoint-config" : "404, device not found",
                    ErrorMessage);
                return null;
            }

            var decryptedEventId = resolution.EventId;

            // Load event for timezone
            var eventEntity = await _unitOfWork.GetRepository<Event>()
                .GetQuery(e => e.Id == decryptedEventId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (eventEntity == null)
            {
                ErrorMessage = $"Device '{identifiers[0]}' resolves to event {decryptedEventId}, which was not found.";
                _logger.LogWarning(
                    "live-readings: REJECTED (controller maps to 404) — {Error}", ErrorMessage);
                return null;
            }

            var device = await _unitOfWork.GetRepository<Device>()
                .GetQuery(d => d.Id == resolution.DeviceDbId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (device == null)
            {
                ErrorMessage = $"Device '{identifiers[0]}' is not registered. Add it via the admin UI first.";
                _logger.LogWarning(
                    "live-readings: REJECTED (controller maps to 404) — {Error}", ErrorMessage);
                return null;
            }

            // Resolve event timezone
            TimeZoneInfo eventTz;
            var tzId = eventEntity.TimeZone ?? "UTC";
            try { eventTz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch { eventTz = TimeZoneInfo.Utc; tzId = "UTC"; }

            // Get or create today's live batch for this device — EVENT-level (RaceId NULL),
            // the offline event-upload shape: every race's pipeline run includes it
            // (b.RaceId == raceId || b.RaceId == null).
            var batch = await GetOrCreateBatchAsync(device, decryptedEventId, ct);

            // Map incoming rows to RawRFIDReading entities
            var rawReadings = request.Readings
                .Where(r => !string.IsNullOrEmpty(r.Epc))
                .Select(r => MapToRawReading(r, batch.Id, device, eventTz, tzId))
                .ToList();

            int skipped = request.Readings.Count - rawReadings.Count;

            if (rawReadings.Count == 0)
                return new LiveReadingResponse
                {
                    Skipped = skipped,
                    BatchId = _encryptionService.Encrypt(batch.Id.ToString())
                };

            // Save raw readings
            var readingRepo = _unitOfWork.GetRepository<RawRFIDReading>();
            foreach (var r in rawReadings)
                await readingRepo.AddAsync(r);
            await _unitOfWork.SaveChangesAsync();

            // Update batch statistics
            var uniqueEpcs = await readingRepo
                .GetQuery(r => r.BatchId == batch.Id && r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                .Select(r => r.Epc)
                .Distinct()
                .CountAsync(ct);

            batch.TotalReadings += rawReadings.Count;
            batch.UniqueEpcs = uniqueEpcs;
            batch.TimeRangeEnd = rawReadings.Max(r => r.TimestampMs);
            if (batch.TimeRangeStart == 0) batch.TimeRangeStart = rawReadings.Min(r => r.TimestampMs);

            await _unitOfWork.GetRepository<UploadBatch>().UpdateAsync(batch);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogDebug(
                "LiveReadings: saved {Count} readings from device {DeviceId} into batch {BatchId}",
                rawReadings.Count, device.Name, batch.Id);

            // EPC → participant map for THIS request (event-scoped): feeds the immediate
            // SignalR push AND names the RACES that actually received data — the documented
            // offline rule ("race association via EPC → Participant → RaceId") applied to
            // decide which race pipelines to run.
            var epcToParticipant = await LoadEpcParticipantsAsync(rawReadings, decryptedEventId, ct);

            // Push immediate SignalR crossing events before pipeline runs
            await PushCrossingEventsAsync(rawReadings, device, epcToParticipant, ct);

            // Fire-and-forget: run the full pipeline for EVERY race this request's chips
            // belong to (a device can serve multiple races). A FRESH DI scope per race —
            // sequential runs on one scope would accumulate tracked entities across runs
            // (the NoTracking double-attach class).
            var affectedRaceIds = epcToParticipant.Values
                .Select(p => p.RaceId)
                .Distinct()
                .ToList();
            var encryptedEventIdForPipeline = _encryptionService.Encrypt(decryptedEventId.ToString());

            if (affectedRaceIds.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    foreach (var affectedRaceId in affectedRaceIds)
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var rfidService = scope.ServiceProvider.GetRequiredService<IRFIDImportService>();
                            var encryptedRaceId = scope.ServiceProvider
                                .GetRequiredService<IEncryptionService>()
                                .Encrypt(affectedRaceId.ToString());
                            await rfidService.ProcessCompleteWorkflowAsync(encryptedEventIdForPipeline, encryptedRaceId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Background pipeline failed for event {EventId} race {RaceId}",
                                decryptedEventId, affectedRaceId);
                        }
                    }
                });
            }

            return new LiveReadingResponse
            {
                Accepted = rawReadings.Count,
                Skipped = skipped,
                BatchId = _encryptionService.Encrypt(batch.Id.ToString())
            };
        }

        // ── Batch management ──────────────────────────────────────────────────────

        private async Task<UploadBatch> GetOrCreateBatchAsync(
            Device device, int eventId, CancellationToken ct)
        {
            var today = DateTime.UtcNow.Date;
            var deviceId = !string.IsNullOrEmpty(device.DeviceMacAddress)
                ? device.DeviceMacAddress
                : device.Name ?? device.Id.ToString();

            var existing = await _unitOfWork.GetRepository<UploadBatch>()
                .GetQuery(b =>
                    b.DeviceId == deviceId &&
                    b.EventId == eventId &&
                    b.RaceId == null &&
                    b.SourceType == "online_webhook" &&
                    b.ProcessingStartedAt != null &&
                    b.ProcessingStartedAt.Value.Date == today &&
                    b.AuditProperties.IsActive &&
                    !b.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (existing != null) return existing;

            var batch = new UploadBatch
            {
                // EVENT-level (RaceId NULL) — the offline import-auto shape: race
                // association happens per read during processing, and every race's
                // pipeline run includes event-level batches.
                RaceId = null,
                EventId = eventId,
                DeviceId = deviceId,
                OriginalFileName = $"live_{deviceId}_{today:yyyy-MM-dd}",
                StoredFilePath = null,
                FileSizeBytes = 0,
                FileHash = $"live_{deviceId}_{eventId}_{today:yyyyMMdd}",
                FileFormat = "LIVE",
                Status = "uploading",
                SourceType = "online_webhook",
                IsLiveSync = true,
                ProcessingStartedAt = DateTime.UtcNow,
                AuditProperties = new AuditProperties
                {
                    CreatedBy = 0,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                }
            };

            await _unitOfWork.GetRepository<UploadBatch>().AddAsync(batch);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "Created live event-level UploadBatch {BatchId} for device {DeviceId} event {EventId} (RaceId NULL)",
                batch.Id, deviceId, eventId);

            return batch;
        }

        // ── Reading mapping ───────────────────────────────────────────────────────

        private static RawRFIDReading MapToRawReading(
            LiveReadingDto dto, int batchId, Device device, TimeZoneInfo tz, string tzId)
        {
            var readTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(dto.Time).UtcDateTime;
            var readTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(readTimeUtc, tz);
            var deviceId = !string.IsNullOrEmpty(device.DeviceMacAddress)
                ? device.DeviceMacAddress
                : device.Name ?? device.Id.ToString();

            return new RawRFIDReading
            {
                BatchId = batchId,
                DeviceId = deviceId,
                Epc = dto.Epc.Replace(" ", "").ToUpperInvariant(),
                TimestampMs = dto.Time,
                Antenna = dto.Antenna,
                RssiDbm = dto.Rssi,
                Channel = dto.Channel,
                ReadTimeLocal = readTimeLocal,
                ReadTimeUtc = readTimeUtc,
                TimeZoneId = tzId,
                ProcessResult = "Pending",
                SourceType = "online_webhook",
                AuditProperties = new AuditProperties
                {
                    CreatedBy = 0,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                }
            };
        }

        // ── EPC → participant resolution (SignalR push + affected-race discovery) ──

        private sealed record LiveEpcParticipant(
            string Epc, int ParticipantId, string? BibNumber, string FirstName, string LastName, int RaceId);

        private async Task<Dictionary<string, LiveEpcParticipant>> LoadEpcParticipantsAsync(
            List<RawRFIDReading> readings, int eventId, CancellationToken ct)
        {
            var epcs = readings.Select(r => r.Epc).Distinct().ToList();

            return await _unitOfWork.GetRepository<ChipAssignment>()
                .GetQuery(ca =>
                    ca.Event.Id == eventId &&
                    !ca.UnassignedAt.HasValue &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted &&
                    epcs.Contains(ca.Chip.EPC))
                .AsNoTracking()
                .Select(ca => new LiveEpcParticipant(
                    ca.Chip.EPC,
                    ca.ParticipantId,
                    ca.Participant.BibNumber,
                    ca.Participant.FirstName ?? "",
                    ca.Participant.LastName ?? "",
                    ca.Participant.RaceId))
                .ToDictionaryAsync(x => x.Epc, x => x, ct);
        }

        // ── SignalR immediate feedback ────────────────────────────────────────────

        private async Task PushCrossingEventsAsync(
            List<RawRFIDReading> readings,
            Device device,
            Dictionary<string, LiveEpcParticipant> epcToParticipant,
            CancellationToken ct)
        {
            try
            {
                if (epcToParticipant.Count == 0) return;

                var checkpoint = await _unitOfWork.GetRepository<Checkpoint>()
                    .GetQuery(cp =>
                        cp.DeviceId == device.Id &&
                        cp.AuditProperties.IsActive &&
                        !cp.AuditProperties.IsDeleted)
                    .OrderByDescending(cp => cp.AuditProperties.CreatedDate)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                var crossings = new List<CheckpointCrossingEvent>();

                foreach (var reading in readings)
                {
                    if (!epcToParticipant.TryGetValue(reading.Epc, out var p)) continue;

                    crossings.Add(new CheckpointCrossingEvent
                    {
                        ParticipantName = $"{p.FirstName} {p.LastName}".Trim(),
                        BibNumber = p.BibNumber ?? "",
                        CheckpointName = checkpoint?.Name ?? device.Name ?? "Mat",
                        Epc = reading.Epc,
                        Timestamp = reading.ReadTimeUtc,
                        Rssi = (double)(reading.RssiDbm ?? 0),
                        AntennaPort = reading.Antenna ?? 0,
                        RaceId = p.RaceId,
                        CheckpointId = checkpoint?.Id ?? 0
                    });
                }

                foreach (var group in crossings.GroupBy(e => e.RaceId))
                {
                    await _raceHub.Clients.Group($"race-{group.Key}")
                        .SendAsync("CheckpointCrossings", group.ToList(), ct);
                }

                _logger.LogDebug(
                    "LiveReadings: pushed {Count} SignalR crossings from {DeviceName}",
                    crossings.Count, device.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR push failed — data already saved, continuing");
            }
        }
    }
}
