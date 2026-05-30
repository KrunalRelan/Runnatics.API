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
        private readonly IHubContext<RaceHub> _raceHub;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LiveReadingService> _logger;

        public LiveReadingService(
            IUnitOfWork<RaceSyncDbContext> unitOfWork,
            IEncryptionService encryptionService,
            IHubContext<RaceHub> raceHub,
            IServiceScopeFactory scopeFactory,
            ILogger<LiveReadingService> logger)
        {
            _unitOfWork = unitOfWork;
            _encryptionService = encryptionService;
            _raceHub = raceHub;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<LiveReadingResponse?> IngestAsync(
            string eventId,
            string raceId,
            LiveReadingsRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
            {
                ErrorMessage = "DeviceId is required.";
                return null;
            }

            if (request.Readings == null || request.Readings.Count == 0)
                return new LiveReadingResponse();

            int decryptedEventId;
            int decryptedRaceId;
            try
            {
                decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            }
            catch
            {
                ErrorMessage = "Invalid eventId or raceId.";
                return null;
            }

            // Load event for TenantId + timezone
            var eventEntity = await _unitOfWork.GetRepository<Event>()
                .GetQuery(e => e.Id == decryptedEventId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (eventEntity == null)
            {
                ErrorMessage = "Event not found.";
                return null;
            }

            // Resolve device by MAC address
            var macNormalized = request.DeviceId.Replace(":", "").Replace("-", "").ToLowerInvariant();
            var device = await _unitOfWork.GetRepository<Device>()
                .GetQuery(d =>
                    (d.DeviceMacAddress == macNormalized || d.Name == request.DeviceId) &&
                    d.TenantId == eventEntity.TenantId &&
                    d.AuditProperties.IsActive &&
                    !d.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (device == null)
            {
                ErrorMessage = $"Device '{request.DeviceId}' is not registered. Add it via the admin UI first.";
                return null;
            }

            // Resolve event timezone
            TimeZoneInfo eventTz;
            var tzId = eventEntity.TimeZone ?? "UTC";
            try { eventTz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch { eventTz = TimeZoneInfo.Utc; tzId = "UTC"; }

            // Get or create today's live batch for this device + race
            var batch = await GetOrCreateBatchAsync(device, decryptedEventId, decryptedRaceId, ct);

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

            // Push immediate SignalR crossing events before pipeline runs
            await PushCrossingEventsAsync(rawReadings, device, decryptedEventId, ct);

            // Fire-and-forget: run full pipeline (dedup → normalize → split times → rankings)
            // A new DI scope is created so scoped services are not disposed under us
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var rfidService = scope.ServiceProvider.GetRequiredService<IRFIDImportService>();
                    await rfidService.ProcessCompleteWorkflowAsync(eventId, raceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Background pipeline failed for event {EventId} race {RaceId}", eventId, raceId);
                }
            });

            return new LiveReadingResponse
            {
                Accepted = rawReadings.Count,
                Skipped = skipped,
                BatchId = _encryptionService.Encrypt(batch.Id.ToString())
            };
        }

        // ── Batch management ──────────────────────────────────────────────────────

        private async Task<UploadBatch> GetOrCreateBatchAsync(
            Device device, int eventId, int raceId, CancellationToken ct)
        {
            var today = DateTime.UtcNow.Date;
            var deviceId = !string.IsNullOrEmpty(device.DeviceMacAddress)
                ? device.DeviceMacAddress
                : device.Name ?? device.Id.ToString();

            var existing = await _unitOfWork.GetRepository<UploadBatch>()
                .GetQuery(b =>
                    b.DeviceId == deviceId &&
                    b.EventId == eventId &&
                    b.RaceId == raceId &&
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
                RaceId = raceId,
                EventId = eventId,
                DeviceId = deviceId,
                OriginalFileName = $"live_{deviceId}_{today:yyyy-MM-dd}",
                StoredFilePath = null,
                FileSizeBytes = 0,
                FileHash = $"live_{deviceId}_{eventId}_{raceId}_{today:yyyyMMdd}",
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
                "Created live UploadBatch {BatchId} for device {DeviceId} event {EventId} race {RaceId}",
                batch.Id, deviceId, eventId, raceId);

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

        // ── SignalR immediate feedback ────────────────────────────────────────────

        private async Task PushCrossingEventsAsync(
            List<RawRFIDReading> readings, Device device, int eventId, CancellationToken ct)
        {
            try
            {
                var epcs = readings.Select(r => r.Epc).Distinct().ToList();

                var epcToParticipant = await _unitOfWork.GetRepository<ChipAssignment>()
                    .GetQuery(ca =>
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
                    .ToDictionaryAsync(x => x.EPC, x => x, ct);

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
