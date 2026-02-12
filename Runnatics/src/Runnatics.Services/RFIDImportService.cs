using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using Runnatics.Services.RFID;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace Runnatics.Services
{
    public class RFIDImportService(IUnitOfWork<RaceSyncDbContext> repository, IMapper mapper, ILogger<RFIDImportService> logger, IUserContextService userContext, IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IRFIDImportService
    {
        private readonly IMapper _mapper = mapper;
        private readonly ILogger<RFIDImportService> _logger = logger;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;

        /// <summary>
        /// Default deduplication window in seconds.
        /// Readings from the same EPC within this window are treated as a single pass.
        /// TODO: Make this configurable per race/event (discuss with client).
        /// </summary>
        private const double DEFAULT_DEDUP_WINDOW_SECONDS = 30.0;

        // =====================================================================
        // FIX: Deduplication helper methods for loop race support
        // =====================================================================

        /// <summary>
        /// Deduplicates readings within a time window. Readings from the same EPC
        /// within dedupWindowSeconds are treated as a single pass.
        /// Returns one representative reading per pass.
        /// For start checkpoint: picks LAST reading (runner leaving mat).
        /// For other checkpoints: picks strongest RSSI signal (best timing accuracy).
        /// </summary>
        private List<RawRFIDReading> DeduplicateReadingsPerPass(List<RawRFIDReading> readings, double dedupWindowSeconds = DEFAULT_DEDUP_WINDOW_SECONDS, bool isStartCheckpoint = false)  // ADD THIS PARAMETER
        {
            if (readings.Count <= 1) return [.. readings];

            var sorted = readings.OrderBy(r => r.TimestampMs).ToList();
            var result = new List<RawRFIDReading>();
            var currentGroup = new List<RawRFIDReading> { sorted[0] };

            for (int i = 1; i < sorted.Count; i++)
            {
                var timeDiffMs = sorted[i].TimestampMs - currentGroup[0].TimestampMs;
                var timeDiffSeconds = timeDiffMs / 1000.0;

                if (timeDiffSeconds <= dedupWindowSeconds)
                {
                    currentGroup.Add(sorted[i]);
                }
                else
                {
                    // CHANGE: Add isStartCheckpoint parameter
                    result.Add(PickBestReadingFromGroup(currentGroup, isStartCheckpoint));
                    currentGroup = new List<RawRFIDReading> { sorted[i] };
                }
            }

            // CHANGE: Add isStartCheckpoint parameter
            result.Add(PickBestReadingFromGroup(currentGroup, isStartCheckpoint));

            return result;
        }

        /// <summary>
        /// From a group of duplicate readings (same pass), pick the best representative.
        /// For start checkpoint: pick LAST reading (when runner exits mat).
        /// For other checkpoints: pick strongest RSSI (most accurate timing point).
        /// </summary>
        private RawRFIDReading PickBestReadingFromGroup(List<RawRFIDReading> group, bool isStartCheckpoint = false)
        {
            if (group.Count == 1) return group[0];

            // For start checkpoint: pick LAST reading (runner exiting mat)
            // For other checkpoints: pick BEST RSSI (strongest signal = most accurate timing)
            if (isStartCheckpoint)
            {
                return group
                    .OrderByDescending(r => r.TimestampMs)  // Latest timestamp
                    .ThenByDescending(r => r.RssiDbm ?? decimal.MinValue)
                    .First();
            }
            else
            {
                return group
                    .OrderByDescending(r => r.RssiDbm ?? decimal.MinValue)  // Strongest RSSI
                    .ThenBy(r => r.TimestampMs)
                    .First();
            }
        }
        public async Task<EPCMappingImportResponse> UploadEPCMappingAsync(string eventId, string? raceId, EPCMappingImportRequest request)
        {
            var userId = _userContext.UserId;
            var tenantId = _userContext.TenantId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = !string.IsNullOrEmpty(raceId) ? Convert.ToInt32(_encryptionService.Decrypt(raceId)) : (int?)null;

            var response = new EPCMappingImportResponse
            {
                FileName = request.File.FileName,
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting EPC-BIB mapping upload for Event {EventId}, Race {RaceId}", decryptedEventId, decryptedRaceId?.ToString() ?? "All");

                // Validate file
                if (request.File == null || request.File.Length == 0)
                {
                    ErrorMessage = "File is empty or not provided";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Validate event exists
                var eventRepo = _repository.GetRepository<Event>();
                var eventExists = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!eventExists)
                {
                    ErrorMessage = "Event not found or you don't have access";
                    _logger.LogWarning("Upload failed: Event {EventId} not found", decryptedEventId);
                    response.Status = "Failed";
                    return response;
                }

                // Parse CSV/Excel file manually
                var records = new List<(string Epc, string Bib)>();

                using var stream = new MemoryStream();
                await request.File.CopyToAsync(stream);
                stream.Position = 0;

                using var reader = new StreamReader(stream);
                var headerLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(headerLine))
                {
                    ErrorMessage = "File is empty or has no headers";
                    response.Status = "Failed";
                    return response;
                }

                // Find EPC and BIB columns
                var headers = headerLine.Split(',', '\t');
                int? epcColumn = null;
                int? bibColumn = null;

                for (int i = 0; i < headers.Length; i++)
                {
                    var header = headers[i].Trim().ToLower();
                    if (header.Contains("epc") || header.Contains("tag") || header.Contains("rfid"))
                        epcColumn = i;
                    else if (header.Contains("bib") || header.Contains("number"))
                        bibColumn = i;
                }

                if (!epcColumn.HasValue || !bibColumn.HasValue)
                {
                    ErrorMessage = "Could not find EPC and BIB columns in file. Headers should contain 'EPC' and 'BIB'";
                    _logger.LogWarning("Upload failed: Missing EPC or BIB column");
                    response.Status = "Failed";
                    return response;
                }

                // Parse data rows
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = line.Split(',', '\t');
                    if (values.Length > Math.Max(epcColumn.Value, bibColumn.Value))
                    {
                        var epc = values[epcColumn.Value].Trim().Trim('"');
                        var bib = values[bibColumn.Value].Trim().Trim('"');
                        if (!string.IsNullOrEmpty(epc) && !string.IsNullOrEmpty(bib))
                        {
                            records.Add((epc, bib));
                        }
                    }
                }

                // Get participants by BIB number (filtered by race if raceId provided, otherwise all event participants)
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participantQuery = participantRepo.GetQuery(p =>
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted);

                // Filter by race if raceId is provided
                if (decryptedRaceId.HasValue)
                {
                    participantQuery = participantQuery.Where(p => p.RaceId == decryptedRaceId.Value);
                }

                var participants = await participantQuery.ToListAsync();

                var participantsByBib = participants.ToDictionary(p => p.BibNumber ?? string.Empty, p => p);

                // Get or create chips
                var chipRepo = _repository.GetRepository<Chip>();
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();

                var totalRecords = 0;
                var successCount = 0;
                var errorCount = 0;
                var notFoundBibs = new List<string>();
                var errors = new List<string>();

                await _repository.BeginTransactionAsync();

                try
                {
                    var rowNumber = 2; // Start from 2 (after header)
                    foreach (var (epc, bib) in records)
                    {
                        totalRecords++;

                        // Find participant by BIB
                        if (!participantsByBib.TryGetValue(bib, out var participant))
                        {
                            if (!notFoundBibs.Contains(bib))
                            {
                                notFoundBibs.Add(bib);
                            }
                            errors.Add($"Row {rowNumber}: Participant not found with BIB {bib}");
                            errorCount++;
                            rowNumber++;
                            continue;
                        }

                        // Get or create chip
                        var chip = await chipRepo.GetQuery(c =>
                            c.EPC == epc &&
                            c.TenantId == tenantId)
                            .FirstOrDefaultAsync();

                        if (chip == null)
                        {
                            chip = new Chip
                            {
                                TenantId = tenantId,
                                EPC = epc,
                                Status = "Assigned",
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            };
                            await chipRepo.AddAsync(chip);
                            await _repository.SaveChangesAsync(); // Save to get chip ID
                        }
                        else if (chip.Status == "Available")
                        {
                            chip.Status = "Assigned";
                            await chipRepo.UpdateAsync(chip);
                        }

                        // Check if assignment already exists
                        var existingAssignment = await chipAssignmentRepo.GetQuery(ca =>
                            ca.EventId == decryptedEventId &&
                            ca.ParticipantId == participant.Id &&
                            ca.ChipId == chip.Id &&
                            !ca.UnassignedAt.HasValue)
                            .FirstOrDefaultAsync();

                        if (existingAssignment == null)
                        {
                            // Create chip assignment
                            var assignment = new ChipAssignment
                            {
                                EventId = decryptedEventId,
                                ParticipantId = participant.Id,
                                ChipId = chip.Id,
                                AssignedAt = DateTime.UtcNow,
                                AssignedByUserId = userId,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            };
                            await chipAssignmentRepo.AddAsync(assignment);
                        }

                        successCount++;
                        rowNumber++;
                    }

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    _logger.LogInformation(
                        "EPC mapping upload completed. Success: {Success}, Errors: {Errors}, Not Found: {NotFound}",
                        successCount, errorCount, notFoundBibs.Count);
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }

                response.TotalRecords = totalRecords;
                response.SuccessCount = successCount;
                response.ErrorCount = errorCount;
                response.NotFoundBibCount = notFoundBibs.Count;
                response.NotFoundBibs = notFoundBibs;
                response.Errors = errors.Take(100).ToList(); // Limit errors to first 100
                response.Status = errorCount > 0 ? "CompletedWithErrors" : "Completed";

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error uploading EPC mapping: {ex.Message}";
                _logger.LogError(ex, "Error uploading EPC mapping file");
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<RFIDImportResponse> UploadRFIDFileAsync(string eventId, string raceId, RFIDImportRequest request)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

            var response = new RFIDImportResponse
            {
                FileName = request.File.FileName,
                UploadedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            try
            {
                _logger.LogInformation("Starting RFID file upload for Event {EventId}, Race {RaceId}", decryptedEventId, decryptedRaceId);

                // Validate file
                if (request.File == null || request.File.Length == 0)
                {
                    ErrorMessage = "File is empty or not provided";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Check if file is SQLite database
                var isSqlite = request.File.FileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                              request.File.FileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase);

                if (!isSqlite)
                {
                    ErrorMessage = "Only SQLite database files (.db, .sqlite) are supported";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Validate event exists
                var eventRepo = _repository.GetRepository<Event>();
                var eventExists = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!eventExists)
                {
                    ErrorMessage = "Event not found or you don't have access";
                    _logger.LogWarning("Upload failed: Event {EventId} not found", decryptedEventId);
                    response.Status = "Failed";
                    return response;
                }

                // Validate race exists
                var raceRepo = _repository.GetRepository<Race>();
                var raceExists = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!raceExists)
                {
                    ErrorMessage = "Race not found";
                    _logger.LogWarning("Upload failed: Race {RaceId} not found for Event {EventId}", decryptedRaceId, decryptedEventId);
                    response.Status = "Failed";
                    return response;
                }

                // Save file temporarily
                var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Calculate file hash to prevent duplicates
                var fileHash = CalculateFileHash(tempFilePath);

                // Check for duplicate upload
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var existingBatch = await batchRepo.GetQuery(b =>
                    b.FileHash == fileHash &&
                    b.RaceId == decryptedRaceId &&
                    b.AuditProperties.IsActive &&
                    !b.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existingBatch != null)
                {
                    File.Delete(tempFilePath);
                    ErrorMessage = "This file has already been uploaded";
                    response.Status = "Failed";
                    return response;
                }

                // Extract device serial from filename (e.g., "0016251292ae" from "2026-01-25_0016251292ae_(box15).db")
                var deviceSerial = ExtractDeviceNameFromFilename(request.File.FileName);
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    deviceSerial = request.DeviceId ?? "Unknown";
                }

                // Look up the Device record by serial number to find associated checkpoint(s)
                var deviceRepo = _repository.GetRepository<Device>();
                var device = await deviceRepo.GetQuery(d =>
                    d.DeviceId == deviceSerial &&
                    d.AuditProperties.IsActive &&
                    !d.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                int? checkpointId = null;
                bool isLoopRace = false;
                if (device != null)
                {
                    // Find ALL checkpoints mapped to this device (for loop/lap races)
                    var checkpointRepo = _repository.GetRepository<Checkpoint>();
                    var checkpoints = await checkpointRepo.GetQuery(cp =>
                        cp.DeviceId == device.Id &&
                        cp.RaceId == decryptedRaceId &&
                        cp.EventId == decryptedEventId &&
                        cp.AuditProperties.IsActive &&
                        !cp.AuditProperties.IsDeleted)
                        .OrderBy(cp => cp.DistanceFromStart)
                        .AsNoTracking()
                        .ToListAsync();

                    if (checkpoints.Count == 0)
                    {
                        _logger.LogWarning(
                            "Device '{DeviceSerial}' found but no checkpoint assigned for Race {RaceId}. " +
                            "Readings will be uploaded but not assigned to a checkpoint.",
                            deviceSerial, decryptedRaceId);
                    }
                    else if (checkpoints.Count == 1)
                    {
                        // Simple case: Single checkpoint per device
                        checkpointId = checkpoints[0].Id;
                        _logger.LogInformation(
                            "Mapped device '{DeviceSerial}' to checkpoint '{CheckpointName}' (ID: {CheckpointId}) at {Distance} KM",
                            deviceSerial, checkpoints[0].Name, checkpoints[0].Id, checkpoints[0].DistanceFromStart);
                    }
                    else
                    {
                        // Loop/Lap race: Multiple checkpoints use the same device
                        isLoopRace = true;
                        checkpointId = null; // Will be assigned during processing based on timing
                        _logger.LogInformation(
                            "Device '{DeviceSerial}' mapped to {Count} checkpoints (LOOP RACE detected): {Checkpoints}. " +
                            "Readings will be assigned based on participant timing sequence.",
                            deviceSerial, checkpoints.Count,
                            string.Join(", ", checkpoints.Select(cp => $"{cp.Name} ({cp.DistanceFromStart}KM)")));
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Device with serial '{DeviceSerial}' not found in database. " +
                        "Readings will be uploaded but device-to-checkpoint mapping cannot be applied.",
                        deviceSerial);
                }

                // Create batch record
                var batch = new UploadBatch
                {
                    RaceId = decryptedRaceId,
                    EventId = decryptedEventId,
                    DeviceId = deviceSerial,
                    ExpectedCheckpointId = checkpointId,
                    OriginalFileName = request.File.FileName,
                    StoredFilePath = tempFilePath,
                    FileSizeBytes = request.File.Length,
                    FileHash = fileHash,
                    FileFormat = "DB",
                    Status = "uploading",
                    SourceType = "file_upload",
                    IsLiveSync = false,
                    AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        CreatedBy = userId,
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                };

                await batchRepo.AddAsync(batch);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Created UploadBatch {BatchId}", batch.Id);

                // Parse SQLite file (treat as local time by default since most readers use local)
                var readings = await ParseSqliteFileAsync(
                    tempFilePath,
                    batch.Id,
                    deviceSerial,
                    request.TimeZoneId,
                    false // Default: timestamps from reader are in local time
                );

                if (readings.Count == 0)
                {
                    ErrorMessage = "No valid RFID readings found in file";
                    _logger.LogWarning("Upload failed: No valid readings in SQLite file");
                    response.Status = "Failed";
                    File.Delete(tempFilePath);
                    return response;
                }

                // Bulk insert readings
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                foreach (var reading in readings)
                {
                    await readingRepo.AddAsync(reading);
                }
                await _repository.SaveChangesAsync();

                // Update batch statistics
                batch.TotalReadings = readings.Count;
                batch.UniqueEpcs = readings.Select(r => r.Epc).Distinct().Count();
                batch.TimeRangeStart = readings.Min(r => r.TimestampMs);
                batch.TimeRangeEnd = readings.Max(r => r.TimestampMs);
                batch.Status = "uploaded";
                batch.ProcessingStartedAt = DateTime.UtcNow;

                await batchRepo.UpdateAsync(batch);
                await _repository.SaveChangesAsync();

                response.UploadBatchId = _encryptionService.Encrypt(batch.Id.ToString());
                response.TotalReadings = readings.Count;
                response.UniqueEpcs = readings.Select(r => r.Epc).Distinct().Count();
                response.TimeRangeStart = readings.Min(r => r.TimestampMs);
                response.TimeRangeEnd = readings.Max(r => r.TimestampMs);
                response.FileSizeBytes = request.File.Length;
                response.FileFormat = "DB";
                response.Status = "uploaded";

                _logger.LogInformation("RFID file upload completed. Batch: {BatchId}, Readings: {Count}", batch.Id, readings.Count);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error uploading RFID file: {ex.Message}";
                _logger.LogError(ex, "Error uploading RFID file");
                response.Status = "Failed";
                return response;
            }
        }

        /// <summary>
        /// Upload RFID file at event level. RaceId is optional - when not provided, the file is stored
        /// at event level (RaceId = NULL) and race association happens during processing via 
        /// EPC → Participant → RaceId. This is the recommended approach when a single device 
        /// captures data for multiple races.
        /// </summary>
        public async Task<RFIDImportResponse> UploadRFIDFileEventLevelAsync(string eventId, string? raceId, RFIDImportRequest request)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = !string.IsNullOrEmpty(raceId)
                ? Convert.ToInt32(_encryptionService.Decrypt(raceId))
                : (int?)null;

            var response = new RFIDImportResponse
            {
                FileName = request.File.FileName,
                UploadedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            try
            {
                _logger.LogInformation(
                    "Starting event-level RFID file upload for Event {EventId}, Race {RaceId}",
                    decryptedEventId,
                    decryptedRaceId?.ToString() ?? "ALL (event-level)");

                // Validate file
                if (request.File == null || request.File.Length == 0)
                {
                    ErrorMessage = "File is empty or not provided";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Check if file is SQLite database
                var isSqlite = request.File.FileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                              request.File.FileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase);

                if (!isSqlite)
                {
                    ErrorMessage = "Only SQLite database files (.db, .sqlite) are supported";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Validate event exists
                var eventRepo = _repository.GetRepository<Event>();
                var eventExists = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!eventExists)
                {
                    ErrorMessage = "Event not found or you don't have access";
                    _logger.LogWarning("Upload failed: Event {EventId} not found", decryptedEventId);
                    response.Status = "Failed";
                    return response;
                }

                // Validate race exists (only if raceId is provided)
                if (decryptedRaceId.HasValue)
                {
                    var raceRepo = _repository.GetRepository<Race>();
                    var raceExists = await raceRepo.GetQuery(r =>
                        r.Id == decryptedRaceId.Value &&
                        r.EventId == decryptedEventId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .AnyAsync();

                    if (!raceExists)
                    {
                        ErrorMessage = "Race not found";
                        _logger.LogWarning("Upload failed: Race {RaceId} not found for Event {EventId}", decryptedRaceId, decryptedEventId);
                        response.Status = "Failed";
                        return response;
                    }
                }

                // Save file temporarily
                var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Calculate file hash to prevent duplicates
                var fileHash = CalculateFileHash(tempFilePath);

                // Check for duplicate upload at EVENT level (FileHash + EventId)
                // This allows the same file to be uploaded once regardless of how many races it contains
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var existingBatch = await batchRepo.GetQuery(b =>
                    b.FileHash == fileHash &&
                    b.EventId == decryptedEventId &&  // Event-level duplicate check
                    b.AuditProperties.IsActive &&
                    !b.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existingBatch != null)
                {
                    File.Delete(tempFilePath);
                    ErrorMessage = "This file has already been uploaded for this event";
                    response.Status = "Failed";
                    return response;
                }

                // Extract device serial from filename (e.g., "0016251292ae" from "2026-01-25_0016251292ae_(box15).db")
                var deviceSerial = ExtractDeviceNameFromFilename(request.File.FileName);
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    deviceSerial = request.DeviceId ?? "Unknown";
                }

                // For event-level uploads, we don't try to resolve checkpoint at upload time
                // Checkpoint assignment happens during processing based on EPC → Participant → RaceId
                _logger.LogInformation(
                    "Event-level upload: Device '{DeviceSerial}' detected. " +
                    "Checkpoint assignment will be determined during race-level processing via EPC → Participant → RaceId.",
                    deviceSerial);

                // Create batch record with optional RaceId
                var batch = new UploadBatch
                {
                    RaceId = decryptedRaceId,  // NULL for event-level uploads
                    EventId = decryptedEventId,
                    DeviceId = deviceSerial,
                    ExpectedCheckpointId = null,  // Determined during processing
                    OriginalFileName = request.File.FileName,
                    StoredFilePath = tempFilePath,
                    FileSizeBytes = request.File.Length,
                    FileHash = fileHash,
                    FileFormat = "DB",
                    Status = "uploading",
                    SourceType = "file_upload",
                    IsLiveSync = false,
                    AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        CreatedBy = userId,
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                };

                await batchRepo.AddAsync(batch);
                await _repository.SaveChangesAsync();

                _logger.LogInformation(
                    "Created event-level UploadBatch {BatchId} (RaceId: {RaceId})",
                    batch.Id,
                    decryptedRaceId?.ToString() ?? "NULL");

                // Parse SQLite file (treat as local time by default since most readers use local)
                var readings = await ParseSqliteFileAsync(
                    tempFilePath,
                    batch.Id,
                    deviceSerial,
                    request.TimeZoneId,
                    false // Default: timestamps from reader are in local time
                );

                if (readings.Count == 0)
                {
                    ErrorMessage = "No valid RFID readings found in file";
                    _logger.LogWarning("Upload failed: No valid readings in SQLite file");
                    response.Status = "Failed";
                    File.Delete(tempFilePath);
                    return response;
                }

                // Bulk insert readings
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                foreach (var reading in readings)
                {
                    await readingRepo.AddAsync(reading);
                }
                await _repository.SaveChangesAsync();

                // Update batch statistics
                batch.TotalReadings = readings.Count;
                batch.UniqueEpcs = readings.Select(r => r.Epc).Distinct().Count();
                batch.TimeRangeStart = readings.Min(r => r.TimestampMs);
                batch.TimeRangeEnd = readings.Max(r => r.TimestampMs);
                batch.Status = "uploaded";
                batch.ProcessingStartedAt = DateTime.UtcNow;

                await batchRepo.UpdateAsync(batch);
                await _repository.SaveChangesAsync();

                response.UploadBatchId = _encryptionService.Encrypt(batch.Id.ToString());
                response.TotalReadings = readings.Count;
                response.UniqueEpcs = readings.Select(r => r.Epc).Distinct().Count();
                response.TimeRangeStart = readings.Min(r => r.TimestampMs);
                response.TimeRangeEnd = readings.Max(r => r.TimestampMs);
                response.FileSizeBytes = request.File.Length;
                response.FileFormat = "DB";
                response.Status = "uploaded";

                _logger.LogInformation(
                    "Event-level RFID file upload completed. Batch: {BatchId}, Readings: {Count}, UniqueEPCs: {UniqueEPCs}",
                    batch.Id, readings.Count, response.UniqueEpcs);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error uploading RFID file: {ex.Message}";
                _logger.LogError(ex, "Error uploading event-level RFID file");
                response.Status = "Failed";
                return response;
            }
        }

        /// <summary>
        /// Upload RFID file with automatic event/race detection based on device name from filename.
        /// Extracts device name from filename, finds associated checkpoint, and determines event/race context.
        /// </summary>
        public async Task<RFIDImportResponse> UploadRFIDFileAutoAsync(RFIDImportRequest request)
        {
            var tenantId = _userContext.TenantId;

            var response = new RFIDImportResponse
            {
                FileName = request.File?.FileName ?? "Unknown",
                UploadedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            try
            {
                // Validate file
                if (request.File == null || request.File.Length == 0)
                {
                    ErrorMessage = "File is empty or not provided";
                    _logger.LogWarning("Auto upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                _logger.LogInformation("Starting auto-detection RFID file upload for file: {FileName}", request.File.FileName);

                // Extract device name from filename
                var deviceName = ExtractDeviceNameFromFilename(request.File.FileName);
                if (string.IsNullOrEmpty(deviceName))
                {
                    ErrorMessage = "Unable to extract device name from filename. Expected format: DeviceName_timestamp.db or DeviceName-timestamp.db";
                    _logger.LogWarning("Auto upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                _logger.LogInformation("Extracted device name: {DeviceName} from filename: {FileName}", deviceName, request.File.FileName);

                // Find device by name and tenant
                var deviceRepo = _repository.GetRepository<Device>();
                var device = await deviceRepo.GetQuery(d =>
                    d.DeviceId == deviceName &&
                    d.TenantId == tenantId &&
                    d.AuditProperties.IsActive &&
                    !d.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (device == null)
                {
                    ErrorMessage = $"Device '{deviceName}' not found in the system. Please ensure the device is registered.";
                    _logger.LogWarning("Auto upload failed: Device '{DeviceName}' not found for tenant {TenantId}", deviceName, tenantId);
                    response.Status = "Failed";
                    return response;
                }

                _logger.LogInformation("Found device: {DeviceId} for name: {DeviceName}", device.Id, deviceName);

                // Find checkpoint associated with this device
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
                    ErrorMessage = $"No checkpoint is assigned to device '{deviceName}'. Please configure the device checkpoint assignment first.";
                    _logger.LogWarning("Auto upload failed: No checkpoint found for device {DeviceId}", device.Id);
                    response.Status = "Failed";
                    return response;
                }

                _logger.LogInformation("Found checkpoint: {CheckpointId} with Event: {EventId}, Race: {RaceId}",
                    checkpoint.Id, checkpoint.EventId, checkpoint.RaceId);

                // Encrypt EventId for the upload (RaceId is intentionally NULL for event-level uploads)
                var encryptedEventId = _encryptionService.Encrypt(checkpoint.EventId.ToString());

                // Update request with discovered device info
                // Note: ExpectedCheckpointId is left null - it will be determined during processing
                // based on EPC → Participant → RaceId → Checkpoint chain
                request.DeviceId = device.DeviceId;

                _logger.LogInformation(
                    "Auto-detection complete. Delegating to event-level upload with EventId: {EventId} (RaceId: NULL). " +
                    "Race association will be determined during processing via EPC → Participant → RaceId.",
                    checkpoint.EventId);

                // Delegate to event-level upload method with NULL raceId
                // This allows a single file to contain readings for multiple races
                return await UploadRFIDFileEventLevelAsync(encryptedEventId, null, request);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during auto-detection upload: {ex.Message}";
                _logger.LogError(ex, "Error during auto-detection RFID upload");
                response.Status = "Failed";
                return response;
            }
        }

        /// <summary>
        /// Extracts device name from filename.
        /// Expected format: 2025-12-25_001625122652_.db (date_devicename_)
        /// OR: 2026-01-25_00162512dbb0_(box15).db (date_devicename_(suffix))
        /// Device name is extracted from the part AFTER the first underscore and BEFORE any additional underscores or parentheses.
        /// </summary>
        private static string ExtractDeviceNameFromFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            // Remove extension
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrEmpty(nameWithoutExtension))
                return string.Empty;

            // Expected format: 2025-12-25_001625122652_ or 2026-01-25_00162512dbb0_(box15)
            // Device name comes AFTER the first underscore and BEFORE any additional underscores or parentheses
            var underscoreParts = nameWithoutExtension.Split('_');
            if (underscoreParts.Length >= 2)
            {
                // Get the second part (index 1) - this should be the device name
                var devicePart = underscoreParts[1];

                // Remove any trailing underscores or whitespace
                devicePart = devicePart.TrimEnd('_', ' ');

                // Remove any parenthetical suffixes like (box15)
                var parenIndex = devicePart.IndexOf('(');
                if (parenIndex > 0)
                {
                    devicePart = devicePart.Substring(0, parenIndex).Trim();
                }

                return devicePart;
            }

            // Fallback: Try splitting by hyphen if underscore pattern not found
            var hyphenParts = nameWithoutExtension.Split('-');
            if (hyphenParts.Length >= 2)
            {
                // Return the last part (device name)
                return hyphenParts[^1].Trim();
            }

            // If no delimiter found, return the whole name (just the device name)
            return nameWithoutExtension.Trim();
        }

        public async Task<ProcessRFIDImportResponse> ProcessRFIDStagingDataAsync(ProcessRFIDImportRequest request)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));
            var decryptedUploadBatchId = Convert.ToInt32(_encryptionService.Decrypt(request.UploadBatchId));

            var response = new ProcessRFIDImportResponse
            {
                ImportBatchId = decryptedUploadBatchId,
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting RFID processing for UploadBatch {BatchId}", decryptedUploadBatchId);

                // Get import batch
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var importBatch = await batchRepo.GetQuery(b =>
                    b.Id == decryptedUploadBatchId &&
                    b.RaceId == decryptedRaceId &&
                    b.EventId == decryptedEventId)
                    .FirstOrDefaultAsync();

                if (importBatch == null)
                {
                    ErrorMessage = "Import batch not found";
                    _logger.LogWarning("Upload batch {BatchId} not found", decryptedUploadBatchId);
                    response.Status = "Failed";
                    return response;
                }

                // Log device and checkpoint mapping for this batch
                if (importBatch.ExpectedCheckpointId.HasValue)
                {
                    _logger.LogInformation(
                        "Processing batch {BatchId} from device '{DeviceId}' mapped to checkpoint {CheckpointId}",
                        importBatch.Id, importBatch.DeviceId, importBatch.ExpectedCheckpointId.Value);
                }
                else
                {
                    // Check if this is a loop race scenario (device mapped to multiple checkpoints)
                    var deviceRepo = _repository.GetRepository<Device>();
                    var device = await deviceRepo.GetQuery(d =>
                        d.DeviceId == importBatch.DeviceId &&
                        d.AuditProperties.IsActive &&
                        !d.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

                    if (device != null)
                    {
                        var checkpointRepo = _repository.GetRepository<Checkpoint>();
                        var checkpoints = await checkpointRepo.GetQuery(cp =>
                            cp.DeviceId == device.Id &&
                            cp.RaceId == decryptedRaceId &&
                            cp.EventId == decryptedEventId &&
                            cp.AuditProperties.IsActive &&
                            !cp.AuditProperties.IsDeleted)
                            .OrderBy(cp => cp.DistanceFromStart)
                            .AsNoTracking()
                            .ToListAsync();

                        if (checkpoints.Count > 1)
                        {
                            _logger.LogInformation(
                                "Processing batch {BatchId} from device '{DeviceId}' in LOOP RACE mode. " +
                                "Device mapped to {Count} checkpoints: {Checkpoints}. " +
                                "Will assign readings based on participant timing sequence.",
                                importBatch.Id, importBatch.DeviceId ?? "Unknown", checkpoints.Count,
                                string.Join(", ", checkpoints.Select(cp => $"{cp.Name} ({cp.DistanceFromStart}KM)")));
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Processing batch {BatchId} from device '{DeviceId}' with NO checkpoint mapping. " +
                                "Readings will be processed but not assigned to a checkpoint. " +
                                "Please configure Device.DeviceId to Checkpoint.DeviceId mapping in database.",
                                importBatch.Id, importBatch.DeviceId ?? "Unknown");
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Processing batch {BatchId} from device '{DeviceId}' with NO checkpoint mapping. " +
                            "Readings will be processed but not assigned to a checkpoint. " +
                            "Please configure Device.DeviceId to Checkpoint.DeviceId mapping in database.",
                            importBatch.Id, importBatch.DeviceId ?? "Unknown");
                    }
                }

                // Get pending readings
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                var readings = await readingRepo.GetQuery(r =>
                    r.BatchId == decryptedUploadBatchId &&
                    r.ProcessResult == "Pending")
                    .ToListAsync();

                if (readings.Count == 0)
                {
                    ErrorMessage = "No pending readings to process";
                    _logger.LogWarning("No pending readings for UploadBatch {BatchId}", decryptedUploadBatchId);
                    response.Status = "Completed";
                    return response;
                }

                // Get participants with chip assignments for this race
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();

                // Get active chip assignments for participants in this race
                var chipAssignments = await chipAssignmentRepo.GetQuery(ca =>
                    ca.Participant.RaceId == decryptedRaceId &&
                    ca.Participant.EventId == decryptedEventId &&
                    !ca.UnassignedAt.HasValue &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted)
                    .Include(ca => ca.Chip)
                    .Include(ca => ca.Participant)
                    .ToListAsync();

                // Create dictionary: EPC -> Participant
                var participants = chipAssignments
                    .Where(ca => ca.Chip != null && !string.IsNullOrEmpty(ca.Chip.EPC))
                    .ToDictionary(ca => ca.Chip.EPC, ca => ca.Participant);

                var successCount = 0;
                var errorCount = 0;
                var unlinkedEPCs = new List<string>();

                // Prepare lists for bulk operations
                var readingsToUpdate = new List<RawRFIDReading>();
                var assignmentsToAdd = new List<ReadingCheckpointAssignment>();

                await _repository.BeginTransactionAsync();

                try
                {
                    // **LOOP RACE HANDLING**: Check if device has multiple checkpoints
                    bool isLoopRace = false;
                    List<Checkpoint> deviceCheckpoints = new List<Checkpoint>();

                    if (!importBatch.ExpectedCheckpointId.HasValue)
                    {
                        // Get device and check for multiple checkpoints
                        var deviceRepo = _repository.GetRepository<Device>();
                        var device = await deviceRepo.GetQuery(d =>
                            d.DeviceId == importBatch.DeviceId &&
                            d.AuditProperties.IsActive &&
                            !d.AuditProperties.IsDeleted)
                            .AsNoTracking()
                            .FirstOrDefaultAsync();

                        if (device != null)
                        {
                            var checkpointRepo = _repository.GetRepository<Checkpoint>();
                            deviceCheckpoints = await checkpointRepo.GetQuery(cp =>
                                cp.DeviceId == device.Id &&
                                cp.RaceId == decryptedRaceId &&
                                cp.EventId == decryptedEventId &&
                                cp.AuditProperties.IsActive &&
                                !cp.AuditProperties.IsDeleted)
                                .OrderBy(cp => cp.DistanceFromStart)
                                .AsNoTracking()
                                .ToListAsync();

                            isLoopRace = deviceCheckpoints.Count > 1;
                        }
                    }

                    // Check existing assignments in bulk
                    HashSet<long> existingAssignmentIds = new HashSet<long>();
                    if (importBatch.ExpectedCheckpointId.HasValue)
                    {
                        var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                        var readingIds = readings.Select(r => r.Id).ToList();
                        var existing = await assignmentRepo.GetQuery(a =>
                            readingIds.Contains(a.ReadingId) &&
                            a.CheckpointId == importBatch.ExpectedCheckpointId.Value)
                            .Select(a => a.ReadingId)
                            .ToListAsync();

                        existingAssignmentIds = [.. existing];
                    }

                    // =================================================================
                    // FIX: GROUP AND DEDUPLICATE READINGS BY PARTICIPANT for loop race
                    // =================================================================
                    Dictionary<int, List<RawRFIDReading>> readingsByParticipant = new Dictionary<int, List<RawRFIDReading>>();
                    // Also keep deduplicated version for correct index-based checkpoint assignment
                    Dictionary<int, List<RawRFIDReading>> deduplicatedReadingsByParticipant = new Dictionary<int, List<RawRFIDReading>>();

                    // IDENTIFY START CHECKPOINT(S): All checkpoints with DistanceFromStart = 0
                    var startCheckpointIds = deviceCheckpoints
                        .Where(cp => cp.DistanceFromStart == 0)
                        .Select(cp => cp.Id)
                        .ToHashSet();

                    // GET RACE START TIME for filtering start checkpoint readings
                    var raceRepo = _repository.GetRepository<Race>();
                    var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId)
                        .AsNoTracking()
                        .Include(r => r.RaceSettings)
                        .FirstOrDefaultAsync();
                    var raceStartTime = race?.StartTime;
                    var dedupWindowSeconds = race?.RaceSettings?.DedUpSeconds ?? DEFAULT_DEDUP_WINDOW_SECONDS;
                    if (isLoopRace)
                    {
                        // Get EPC->Participant mapping first
                        var epcToParticipantMap = participants.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Id
                        );

                        // Group readings by participant
                        foreach (var reading in readings)
                        {
                            if (epcToParticipantMap.TryGetValue(reading.Epc, out var participantId))
                            {
                                if (!readingsByParticipant.ContainsKey(participantId))
                                {
                                    readingsByParticipant[participantId] = [];
                                }
                                readingsByParticipant[participantId].Add(reading);
                            }
                        }

                        // Sort and DEDUPLICATE each participant's readings
                        foreach (var kvp in readingsByParticipant)
                        {
                            kvp.Value.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));

                            // ================================================================
                            // FIX ISSUE #1: For START checkpoint in loop race, filter by race start time
                            // Use 5-minute window (not 10) to strictly separate start from finish readings
                            // In fast races (5K ~15-20 min), 10-minute window could include finish readings
                            // ================================================================
                            var isFirstPassStart = deviceCheckpoints.Count > 0 &&
                                                    startCheckpointIds.Contains(deviceCheckpoints[0].Id);

                            List<RawRFIDReading> readingsToDedup = kvp.Value;

                            if (isFirstPassStart)
                            {
                                // Validate race start time is configured
                                if (!raceStartTime.HasValue)
                                {
                                    _logger.LogWarning(
                                        "Loop race: Participant {ParticipantId} has start checkpoint readings but Race.StartTime is not set. " +
                                        "Cannot apply temporal filtering. This may cause incorrect start reading selection.",
                                        kvp.Key);
                                }
                                else
                                {
                                    // Filter to start window (configurable per race, defaults to 5 minutes)
                                    // This ensures we only capture actual start readings, excluding finish readings
                                    // Allows flexibility for different race types (5K = 5 min, marathon = 10-15 min)
                                    var startWindowMinutes = 5; //race.StartWindowMinutes ?? 5.0; // Use Race.StartWindowMinutes if configured
                                    var originalCount = readingsToDedup.Count;

                                    readingsToDedup = kvp.Value
                                        .Where(r =>
                                        {
                                            var minutesSinceStart = (r.ReadTimeUtc - raceStartTime.Value).TotalMinutes;
                                            // Allow readings from 1 minute BEFORE race start (early arrivals at mat)
                                            // up to 5 minutes AFTER race start (normal rolling start window)
                                            return minutesSinceStart >= -1.0 && minutesSinceStart <= startWindowMinutes;
                                        })
                                        .ToList();

                                    if (readingsToDedup.Count < originalCount)
                                    {
                                        _logger.LogInformation(
                                            "Loop race: Participant {ParticipantId} start checkpoint filtered from {Original} to {Filtered} readings " +
                                            "(excluded {Excluded} finish/late readings outside {Window}-minute start window). " +
                                            "Race start: {RaceStart}",
                                            kvp.Key, originalCount, readingsToDedup.Count,
                                            originalCount - readingsToDedup.Count, startWindowMinutes,
                                            raceStartTime.Value.ToString("HH:mm:ss"));
                                    }

                                    if (readingsToDedup.Count == 0)
                                    {
                                        _logger.LogWarning(
                                            "Loop race: Participant {ParticipantId} has {OriginalCount} readings but NONE within {Window}-minute start window " +
                                            "(Race start: {RaceStart}). Earliest reading: {EarliestTime}. " +
                                            "Participant may have DNS (Did Not Start) or Race.StartTime is incorrectly configured.",
                                            kvp.Key, originalCount, startWindowMinutes,
                                            raceStartTime.Value.ToString("HH:mm:ss"),
                                            kvp.Value.OrderBy(r => r.TimestampMs).FirstOrDefault()?.ReadTimeUtc.ToString("HH:mm:ss") ?? "N/A");
                                        deduplicatedReadingsByParticipant[kvp.Key] = [];
                                        continue;
                                    }
                                }
                            }

                            // Deduplicate: readings within dedup window = same pass
                            // For START checkpoint: pick LAST reading from filtered start window (runner exiting mat)
                            // For other checkpoints: pick BEST RSSI (optimal timing accuracy)
                            var deduplicated = DeduplicateReadingsPerPass(readingsToDedup, dedupWindowSeconds, isFirstPassStart);  // ? Picks LAST reading for start, BEST RSSI for others
                            deduplicatedReadingsByParticipant[kvp.Key] = deduplicated;

                            if (kvp.Value.Count != deduplicated.Count)
                            {
                                _logger.LogInformation(
                                    "Loop race dedup: Participant {ParticipantId} had {Original} readings, " +
                                    "deduplicated to {Deduped} unique passes",
                                    kvp.Key, kvp.Value.Count, deduplicated.Count);
                            }
                        }

                        _logger.LogInformation(
                            "Loop race mode: Grouped {TotalReadings} readings into {ParticipantCount} participants, " +
                            "deduplicated to {TotalDeduped} unique passes",
                            readings.Count, readingsByParticipant.Count,
                            deduplicatedReadingsByParticipant.Values.Sum(v => v.Count));
                    }

                    // Process all readings in one pass - no nested queries
                    foreach (var reading in readings)
                    {
                        // Try to link to participant
                        if (participants.TryGetValue(reading.Epc, out var participant))
                        {
                            // Validate signal strength if present
                            if (reading.RssiDbm.HasValue && reading.RssiDbm.Value < -75)
                            {
                                reading.ProcessResult = "Invalid";
                                reading.Notes = "Weak signal (RSSI < -75 dBm)";
                                errorCount++;
                            }
                            else
                            {
                                reading.ProcessResult = "Success";
                                successCount++;

                                // **DETERMINE CHECKPOINT ASSIGNMENT**
                                int? assignedCheckpointId = null;

                                if (isLoopRace && deviceCheckpoints.Count > 0)
                                {
                                    // ==========================================================
                                    // FIX: LOOP RACE MODE — Assign based on DEDUPLICATED passes
                                    // ==========================================================
                                    if (deduplicatedReadingsByParticipant.TryGetValue(participant.Id, out var dedupedReadings))
                                    {
                                        // Find the deduplicated pass this reading belongs to
                                        // A reading belongs to a pass if it's within the dedup window of the pass's representative reading
                                        int passIndex = -1;
                                        for (int pi = 0; pi < dedupedReadings.Count; pi++)
                                        {
                                            var passReading = dedupedReadings[pi];
                                            var timeDiffMs = Math.Abs(reading.TimestampMs - passReading.TimestampMs);
                                            if (timeDiffMs <= (long)(DEFAULT_DEDUP_WINDOW_SECONDS * 1000))
                                            {
                                                passIndex = pi;
                                                break;
                                            }
                                        }

                                        if (passIndex >= 0 && passIndex < deviceCheckpoints.Count)
                                        {
                                            assignedCheckpointId = deviceCheckpoints[passIndex].Id;
                                            _logger.LogDebug(
                                                "Loop race: Participant {ParticipantId} reading at {Time} belongs to pass #{Pass}, " +
                                                "assigned to checkpoint '{CheckpointName}' ({Distance}KM)",
                                                participant.Id, reading.ReadTimeUtc.ToString("HH:mm:ss"),
                                                passIndex + 1, deviceCheckpoints[passIndex].Name,
                                                deviceCheckpoints[passIndex].DistanceFromStart);
                                        }
                                        else if (passIndex >= deviceCheckpoints.Count)
                                        {
                                            _logger.LogWarning(
                                                "Loop race: Participant {ParticipantId} has extra pass #{Pass} beyond {MaxCheckpoints} checkpoints - skipping",
                                                participant.Id, passIndex + 1, deviceCheckpoints.Count);
                                        }
                                        else
                                        {
                                            _logger.LogWarning(
                                                "Loop race: Participant {ParticipantId} reading at {Time} could not be matched to any pass",
                                                participant.Id, reading.ReadTimeUtc.ToString("HH:mm:ss"));
                                        }
                                    }
                                }
                                else if (importBatch.ExpectedCheckpointId.HasValue)
                                {
                                    // **SIMPLE MODE**: Single checkpoint assignment from batch
                                    assignedCheckpointId = importBatch.ExpectedCheckpointId.Value;
                                }

                                // Create checkpoint assignment if determined
                                if (assignedCheckpointId.HasValue && !existingAssignmentIds.Contains(reading.Id))
                                {
                                    assignmentsToAdd.Add(new ReadingCheckpointAssignment
                                    {
                                        ReadingId = reading.Id,
                                        CheckpointId = assignedCheckpointId.Value,
                                        AuditProperties = new Models.Data.Common.AuditProperties
                                        {
                                            CreatedBy = userId,
                                            CreatedDate = DateTime.UtcNow,
                                            IsActive = true,
                                            IsDeleted = false
                                        }
                                    });
                                    reading.AssignmentMethod = isLoopRace ? "LoopRaceSequence" : "DeviceMapping";
                                }
                                else if (!assignedCheckpointId.HasValue)
                                {
                                    _logger.LogWarning(
                                        "Reading {ReadingId} processed successfully but no checkpoint assigned. " +
                                        "Device '{DeviceId}' may not be mapped to a checkpoint in the database.",
                                        reading.Id, importBatch.DeviceId);
                                }
                            }

                            reading.ProcessedAt = DateTime.UtcNow;
                            readingsToUpdate.Add(reading);
                        }
                        else
                        {
                            if (!unlinkedEPCs.Contains(reading.Epc))
                            {
                                unlinkedEPCs.Add(reading.Epc);
                            }
                            reading.ProcessResult = "Invalid";
                            reading.Notes = "No participant found with this RFID tag";
                            reading.ProcessedAt = DateTime.UtcNow;
                            readingsToUpdate.Add(reading);
                            errorCount++;
                        }
                    }

                    // TRUE BULK OPERATIONS - Single DB roundtrip each
                    if (assignmentsToAdd.Count > 0)
                    {
                        var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                        await assignmentRepo.BulkInsertAsync(assignmentsToAdd);
                        _logger.LogInformation("Bulk inserted {Count} checkpoint assignments", assignmentsToAdd.Count);
                    }

                    if (readingsToUpdate.Count > 0)
                    {
                        var readingRepoForUpdate = _repository.GetRepository<RawRFIDReading>();
                        await readingRepoForUpdate.BulkUpdateAsync(readingsToUpdate);
                        _logger.LogInformation("Bulk updated {Count} raw readings", readingsToUpdate.Count);
                    }

                    // Update batch status
                    importBatch.Status = errorCount > 0 ? "completed" : "completed";
                    importBatch.ProcessingCompletedAt = DateTime.UtcNow;
                    await batchRepo.UpdateAsync(importBatch);

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    _logger.LogInformation(
                        "RFID processing completed. Success: {Success}, Errors: {Errors}, Unlinked: {Unlinked}",
                        successCount, errorCount, unlinkedEPCs.Count);
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }

                response.SuccessCount = successCount;
                response.ErrorCount = errorCount;
                response.UnlinkedCount = unlinkedEPCs.Count;
                response.UnlinkedEPCs = unlinkedEPCs;
                response.Status = errorCount > 0 ? "CompletedWithErrors" : "Completed";

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error processing RFID import: {ex.Message}";
                _logger.LogError(ex, "Error processing RFID import batch {BatchId}", decryptedUploadBatchId);
                response.Status = "Failed";
                return response;
            }
        }

        /// <summary>
        /// Process ALL pending RFID batches for an event/race with a single call.
        /// Useful for bulk processing after multiple file uploads.
        /// </summary>
        // ============================================================================
        // UPDATED ProcessCompleteWorkflowAsync — uses new race-level processing
        //
        // OLD FLOW (broken):
        //   Phase 1:   ProcessAllRFIDDataAsync → foreach batch → ProcessRFIDStagingDataAsync
        //              (per-batch loop race assignment can't see full timeline)
        //   Phase 1.5: AssignCheckpointsForLoopRaceAsync
        //              (tries to fix but readings already assigned incorrectly)
        //
        // NEW FLOW:
        //   Phase 1:   ProcessAllStagingDataForRaceAsync
        //              (validates ALL readings at once, assigns ONLY simple devices)
        //   Phase 1.5: AssignCheckpointsForLoopRaceAsync
        //              (assigns ALL shared/loop devices using turnaround algorithm)
        //   Phase 2:   DeduplicateAndNormalizeAsync (unchanged)
        //   Phase 2.5: CreateSplitTimesFromNormalizedReadingsAsync (unchanged)
        //   Phase 3:   CalculateRaceResultsAsync (unchanged)
        // ============================================================================

        public async Task<CompleteRFIDProcessingResponse> ProcessCompleteWorkflowAsync(string eventId, string raceId)
        {
            var overallStartTime = DateTime.UtcNow;
            var response = new CompleteRFIDProcessingResponse
            {
                ProcessedAt = overallStartTime,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting complete RFID processing workflow for race {RaceId}", raceId);

                // ========== PHASE 1: Validate & Process ALL Readings (Race-Level) ==========
                var phase1Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 1: Processing ALL staging data for race...");

                // KEY CHANGE: Single call processes ALL batches at once
                // Groups by EPC, orders by ReadTimeUtc across all batches
                // Assigns checkpoints ONLY for simple devices (1:1 device→checkpoint)
                // Shared/loop devices are left unassigned for Phase 1.5
                var processAllResponse = await ProcessAllStagingDataForRaceAsync(eventId, raceId);

                response.Phase1ProcessingMs = (long)(DateTime.UtcNow - phase1Start).TotalMilliseconds;
                response.TotalBatchesProcessed = processAllResponse.TotalBatches;
                response.SuccessfulBatches = processAllResponse.SuccessfulBatches;
                response.FailedBatches = processAllResponse.FailedBatches;
                response.TotalRawReadingsProcessed = processAllResponse.TotalProcessedReadings;

                if (processAllResponse.Status == "Failed")
                {
                    response.Errors.Add("Phase 1 failed: " + processAllResponse.Message);
                    response.Status = "Failed";
                    return response;
                }

                if (processAllResponse.Status == "NoDataToProcess")
                {
                    response.Warnings.Add("No pending batches found to process");
                }

                _logger.LogInformation(
                    "Phase 1 completed in {Time}ms. Batches: {Batches}, Readings: {Readings}",
                    response.Phase1ProcessingMs,
                    processAllResponse.TotalBatches,
                    processAllResponse.TotalProcessedReadings);

                // ========== PHASE 1.5: Assign Checkpoints for Shared/Loop Devices ==========
                var phase15Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 1.5: Assigning checkpoints for shared/loop devices...");

                // This now processes ALL unassigned readings across all batches
                // using the turnaround-based algorithm with full participant timelines
                var assignResponse = await AssignCheckpointsForLoopRaceAsync(eventId, raceId);

                response.Phase15AssignmentMs = (long)(DateTime.UtcNow - phase15Start).TotalMilliseconds;
                response.CheckpointsAssigned = assignResponse.CheckpointsAssigned;

                if (assignResponse.Status == "Failed")
                {
                    response.Warnings.Add("Phase 1.5 failed: " + (assignResponse.ErrorMessage ?? "Checkpoint assignment error"));
                }

                _logger.LogInformation(
                    "Phase 1.5 completed in {Time}ms. Assigned: {Count}",
                    response.Phase15AssignmentMs,
                    assignResponse.CheckpointsAssigned);

                // ========== PHASE 2: Deduplicate and Normalize ==========
                var phase2Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 2: Deduplicating and normalizing readings...");

                var dedupeResponse = await DeduplicateAndNormalizeAsync(eventId, raceId);

                response.Phase2DeduplicationMs = (long)(DateTime.UtcNow - phase2Start).TotalMilliseconds;
                response.TotalNormalizedReadings = dedupeResponse.NormalizedReadings;
                response.DuplicatesRemoved = dedupeResponse.DuplicatesRemoved;
                response.CheckpointsProcessed = dedupeResponse.CheckpointsProcessed;
                response.ParticipantsProcessed = dedupeResponse.ParticipantsProcessed;

                if (dedupeResponse.Status == "Failed")
                {
                    response.Errors.Add("Phase 2 failed: Deduplication error");
                    response.Status = "Failed";
                    return response;
                }

                if (dedupeResponse.NormalizedReadings == 0)
                {
                    response.Warnings.Add("No new readings to deduplicate");
                }

                _logger.LogInformation(
                    "Phase 2 completed in {Time}ms. Normalized: {Normalized}, Duplicates removed: {Duplicates}",
                    response.Phase2DeduplicationMs,
                    dedupeResponse.NormalizedReadings,
                    dedupeResponse.DuplicatesRemoved);

                // ========== PHASE 2.5: Create Split Times ==========
                var phase25Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 2.5: Creating split times...");

                var splitTimeResponse = await CreateSplitTimesFromNormalizedReadingsAsync(eventId, raceId);

                response.Phase25SplitTimesMs = (long)(DateTime.UtcNow - phase25Start).TotalMilliseconds;
                response.SplitTimesCreated = splitTimeResponse.SplitTimesCreated;

                if (splitTimeResponse.Status == "Failed")
                {
                    response.Warnings.Add("Phase 2.5 failed: Split time creation error (non-critical)");
                }

                _logger.LogInformation(
                    "Phase 2.5 completed in {Time}ms. Split times created: {Count}",
                    response.Phase25SplitTimesMs,
                    splitTimeResponse.SplitTimesCreated);

                // ========== PHASE 3: Calculate Results ==========
                var phase3Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 3: Calculating race results...");

                var calcResponse = await CalculateRaceResultsAsync(eventId, raceId);

                response.Phase3CalculationMs = (long)(DateTime.UtcNow - phase3Start).TotalMilliseconds;
                response.TotalFinishers = calcResponse.TotalFinishers;
                response.ResultsCreated = calcResponse.ResultsCreated;
                response.ResultsUpdated = calcResponse.ResultsUpdated;
                response.DNFCount = calcResponse.DNFCount;
                response.CategoriesProcessed = calcResponse.CategoriesProcessed;
                response.GenderStats = calcResponse.GenderStats;

                if (calcResponse.Status == "Failed")
                {
                    response.Errors.Add("Phase 3 failed: Results calculation error");
                    response.Status = "Failed";
                    return response;
                }

                if (calcResponse.TotalFinishers == 0)
                {
                    response.Warnings.Add("No finishers found. Ensure checkpoints are properly configured.");
                }

                _logger.LogInformation(
                    "Phase 3 completed in {Time}ms. Finishers: {Finishers}, Created: {Created}, Updated: {Updated}",
                    response.Phase3CalculationMs,
                    calcResponse.TotalFinishers,
                    calcResponse.ResultsCreated,
                    calcResponse.ResultsUpdated);

                // ========== Final Summary ==========
                var overallEndTime = DateTime.UtcNow;
                response.TotalProcessingTimeMs = (long)(overallEndTime - overallStartTime).TotalMilliseconds;

                response.Status = response.Errors.Count > 0 ? "CompletedWithErrors" : "Completed";
                response.Message = $"Complete workflow finished: {response.TotalFinishers} finishers processed across {response.CheckpointsProcessed} checkpoints";

                _logger.LogInformation(
                    "═══ Complete RFID workflow finished in {TotalTime}ms ═══\n" +
                    "  Phase 1  (Validate):   {P1}ms — {Batches} batches, {Readings} readings\n" +
                    "  Phase 1.5 (Assign):    {P15}ms — {Assigned} checkpoint assignments\n" +
                    "  Phase 2  (Normalize):  {P2}ms — {Normalized} normalized, {Dupes} dupes removed\n" +
                    "  Phase 2.5 (Splits):    {P25}ms — {Splits} split times\n" +
                    "  Phase 3  (Results):    {P3}ms — {Finishers} finishers\n" +
                    "  Status: {Status}",
                    response.TotalProcessingTimeMs,
                    response.Phase1ProcessingMs, response.TotalBatchesProcessed, response.TotalRawReadingsProcessed,
                    response.Phase15AssignmentMs, response.CheckpointsAssigned,
                    response.Phase2DeduplicationMs, response.TotalNormalizedReadings, response.DuplicatesRemoved,
                    response.Phase25SplitTimesMs, response.SplitTimesCreated,
                    response.Phase3CalculationMs, response.TotalFinishers,
                    response.Status);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during complete RFID workflow: {ex.Message}";
                _logger.LogError(ex, "Error during complete RFID processing workflow");
                response.Status = "Failed";
                response.Errors.Add($"Unexpected error: {ex.Message}");
                response.TotalProcessingTimeMs = (long)(DateTime.UtcNow - overallStartTime).TotalMilliseconds;
                return response;
            }
        }

        private async Task<List<RawRFIDReading>> ParseSqliteFileAsync(string filePath, int batchId, string deviceId, string timeZoneId, bool treatAsUtc)
        {
            var readings = new List<RawRFIDReading>();
            var userId = _userContext.UserId;

            using var connection = new SQLiteConnection($"Data Source={filePath};Read Only=True;");
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, epc, time, antenna, rssi, channel FROM tags ORDER BY time";

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var timestampMs = reader.GetInt64(2); // time column

                // Convert timestamp to DateTime
                DateTime readTimeUtc;
                DateTime readTimeLocal;

                if (treatAsUtc)
                {
                    // Timestamp is already UTC
                    readTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    readTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(readTimeUtc, tz);
                }
                else
                {
                    // Timestamp is in local time (most RFID readers)
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                    readTimeLocal = epoch.AddMilliseconds(timestampMs);

                    var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    readTimeUtc = TimeZoneInfo.ConvertTimeToUtc(readTimeLocal, tz);
                }

                readings.Add(new RawRFIDReading
                {
                    BatchId = batchId,
                    DeviceId = deviceId,
                    Epc = reader.GetString(1),
                    TimestampMs = timestampMs,
                    Antenna = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    RssiDbm = reader.IsDBNull(4) ? null : (decimal?)reader.GetDouble(4),
                    Channel = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    ReadTimeLocal = readTimeLocal,
                    ReadTimeUtc = readTimeUtc,
                    TimeZoneId = timeZoneId,
                    ProcessResult = "Pending",
                    SourceType = "file_upload",
                    AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        CreatedBy = userId,
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                });
            }

            return readings;
        }

        private static string CalculateFileHash(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public async Task<DeduplicationResponse> DeduplicateAndNormalizeAsync(string eventId, string raceId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var startTime = DateTime.UtcNow;

            var response = new DeduplicationResponse
            {
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting deduplication and normalization for Race {RaceId}", decryptedRaceId);

                // Get race start time
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId)
                    .FirstOrDefaultAsync();

                if (race == null)
                {
                    ErrorMessage = "Race not found";
                    response.Status = "Failed";
                    return response;
                }

                var raceStartTime = race.StartTime;

                // =====================================================================
                // FIX #3: Validate Race.StartTime before processing
                // =====================================================================
                if (!raceStartTime.HasValue)
                {
                    ErrorMessage = "Race.StartTime is not set. Please configure the race start time before processing.";
                    _logger.LogError("Race {RaceId} has no StartTime configured", decryptedRaceId);
                    response.Status = "Failed";
                    return response;
                }

                // Get all successfully processed readings with checkpoint assignments
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();

                // **PARENT-CHILD CHECKPOINT MERGING**: Load all checkpoints to build parent-child mapping
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var allCheckpoints = await checkpointRepo.GetQuery(cp =>
                    cp.RaceId == decryptedRaceId &&
                    cp.EventId == decryptedEventId &&
                    cp.AuditProperties.IsActive &&
                    !cp.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                // Build mapping: Child DeviceId -> Parent CheckpointId
                // If a checkpoint has ParentDeviceId > 0, it's a child device and readings should be merged to parent
                var deviceRepo = _repository.GetRepository<Device>();
                var allDevices = await deviceRepo.GetQuery(d =>
                    d.AuditProperties.IsActive &&
                    !d.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                // Create Device.Id -> Device mapping for lookup
                var deviceById = allDevices.ToDictionary(d => d.Id, d => d);

                // Build child checkpoint -> parent checkpoint mapping
                // Key: Child Checkpoint ID, Value: Parent Checkpoint ID
                var childToParentCheckpointMap = new Dictionary<int, int>();

                foreach (var checkpoint in allCheckpoints)
                {
                    // If ParentDeviceId is set and > 0, this checkpoint uses a child device
                    if (checkpoint.ParentDeviceId.HasValue && checkpoint.ParentDeviceId.Value > 0)
                    {
                        // Find the parent checkpoint that has DeviceId == this checkpoint's ParentDeviceId
                        var parentCheckpoint = allCheckpoints.FirstOrDefault(cp =>
                            cp.DeviceId == checkpoint.ParentDeviceId.Value &&
                            Math.Abs(cp.DistanceFromStart - checkpoint.DistanceFromStart) < 0.001m); // Same distance

                        if (parentCheckpoint != null)
                        {
                            childToParentCheckpointMap[checkpoint.Id] = parentCheckpoint.Id;
                            _logger.LogInformation(
                                "Mapping child checkpoint {ChildId} (Device {ChildDevice}) to parent checkpoint {ParentId} '{ParentName}' (Device {ParentDevice}) at {Distance} KM",
                                checkpoint.Id, checkpoint.DeviceId, parentCheckpoint.Id, parentCheckpoint.Name,
                                parentCheckpoint.DeviceId, parentCheckpoint.DistanceFromStart);
                        }
                    }
                }

                _logger.LogInformation("Built parent-child checkpoint mapping: {Count} child checkpoints mapped to parents",
                    childToParentCheckpointMap.Count);

                // =====================================================================
                // FIX: Identify start checkpoint for special handling (pick LAST entry)
                // =====================================================================
                var startCheckpointId = allCheckpoints
                    .OrderBy(cp => cp.DistanceFromStart)
                    .FirstOrDefault()?.Id ?? 0;

                var finishCheckpointId = allCheckpoints
                    .OrderByDescending(cp => cp.DistanceFromStart)
                    .FirstOrDefault()?.Id ?? 0;

                _logger.LogInformation(
                    "Start checkpoint ID: {StartId}, Finish checkpoint ID: {FinishId}",
                    startCheckpointId, finishCheckpointId);

                // First, get already normalized reading IDs for THIS race to filter them out early
                // Only filter by non-null RawReadId values (manual entries have null RawReadId)
                var existingNormalizedReadIds = await normalizedRepo.GetQuery(n =>
                        n.EventId == decryptedEventId &&
                        n.RawReadId.HasValue &&
                        n.AuditProperties.IsActive &&
                        !n.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(n => n.RawReadId!.Value)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} existing normalized readings for event {EventId}",
                    existingNormalizedReadIds.Count, decryptedEventId);

                // Get active chip assignments for this race with their EPCs
                var chipAssignmentsWithEpc = await chipAssignmentRepo.GetQuery(ca =>
                        ca.Participant.RaceId == decryptedRaceId &&
                        !ca.UnassignedAt.HasValue &&
                        ca.AuditProperties.IsActive &&
                        !ca.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(ca => new { ca.ParticipantId, EPC = ca.Chip.EPC })
                    .ToListAsync();

                _logger.LogInformation("Found {Count} active chip assignments for race {RaceId}",
                    chipAssignmentsWithEpc.Count, decryptedRaceId);

                // Create a lookup from EPC to ParticipantId
                var epcToParticipant = chipAssignmentsWithEpc
                    .GroupBy(ca => ca.EPC)
                    .ToDictionary(g => g.Key, g => g.First().ParticipantId);

                // Get reading checkpoint assignments that are active
                var activeAssignments = await assignmentRepo.GetQuery(a =>
                        a.AuditProperties.IsActive &&
                        !a.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(a => new { a.ReadingId, a.CheckpointId })
                    .ToListAsync();

                _logger.LogInformation("Found {Count} active checkpoint assignments", activeAssignments.Count);

                // Create a lookup from ReadingId to CheckpointId
                var readingToCheckpoint = activeAssignments.ToDictionary(a => a.ReadingId, a => a.CheckpointId);

                // Get all active EPCs to filter raw readings
                var activeEpcs = epcToParticipant.Keys.ToHashSet();

                // =====================================================================
                // FIX: Get batch IDs for THIS RACE including event-level batches
                // Event-level batches (RaceId = NULL) contain data for all races
                // =====================================================================
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var raceBatchIds = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    (b.RaceId == decryptedRaceId || b.RaceId == null) &&  // Include event-level batches
                    b.AuditProperties.IsActive &&
                    !b.AuditProperties.IsDeleted)
                    .Select(b => b.Id)
                    .ToListAsync();

                _logger.LogInformation(
                    "Found {Count} upload batches for Race {RaceId} (including event-level batches)",
                    raceBatchIds.Count, decryptedRaceId);

                // Get raw readings that haven't been normalized yet
                // FIX: Added raceBatchIds.Contains(r.BatchId) to filter by race
                var rawReadingsQuery = await readingRepo.GetQuery(r =>
                        r.ProcessResult == "Success" &&
                        raceBatchIds.Contains(r.BatchId) &&  // Only readings from THIS race's batches
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted &&
                        activeEpcs.Contains(r.Epc)) // Only readings for assigned chips
                    .AsNoTracking()
                    .ToListAsync();

                _logger.LogInformation("Found {Count} raw readings with matching EPCs", rawReadingsQuery.Count);

                // Filter and join in memory for better control
                var rawReadings = rawReadingsQuery
                    .Where(r => !existingNormalizedReadIds.Contains(r.Id)) // Exclude already normalized
                    .Where(r => readingToCheckpoint.ContainsKey(r.Id)) // Must have checkpoint assignment
                    .Select(r => new
                    {
                        Reading = r,
                        CheckpointId = readingToCheckpoint[r.Id],
                        ParticipantId = epcToParticipant[r.Epc],
                        RawReadId = r.Id
                    })
                    .ToList();

                _logger.LogInformation("After filtering: {Count} raw readings to process (excluded {Excluded} already normalized, {NoCheckpoint} without checkpoint assignment)",
                    rawReadings.Count,
                    rawReadingsQuery.Count(r => existingNormalizedReadIds.Contains(r.Id)),
                    rawReadingsQuery.Count(r => !readingToCheckpoint.ContainsKey(r.Id)));

                response.TotalRawReadings = rawReadings.Count;

                // =====================================================================
                // FIX #3 (EXTENDED): Validate Race.StartTime against actual reading times
                // =====================================================================
                if (rawReadings.Count > 0)
                {
                    var earliestReading = rawReadings.Min(r => r.Reading.ReadTimeUtc);
                    var daysDiff = Math.Abs((raceStartTime.Value - earliestReading).TotalDays);

                    if (daysDiff > 1)
                    {
                        ErrorMessage = $"Race.StartTime ({raceStartTime.Value:yyyy-MM-dd HH:mm:ss}) differs from earliest reading ({earliestReading:yyyy-MM-dd HH:mm:ss}) by {daysDiff:F1} days. Please fix Race.StartTime in database.";
                        _logger.LogError(
                            "Race.StartTime validation failed for Race {RaceId}. StartTime: {StartTime}, Earliest reading: {EarliestReading}, Diff: {Diff} days",
                            decryptedRaceId, raceStartTime.Value, earliestReading, daysDiff);
                        response.Status = "Failed";
                        return response;
                    }

                    var minutesDiff = (earliestReading - raceStartTime.Value).TotalMinutes;
                    if (minutesDiff < -60)
                    {
                        ErrorMessage = $"Race.StartTime ({raceStartTime.Value:yyyy-MM-dd HH:mm:ss}) is more than 1 hour AFTER the earliest reading ({earliestReading:yyyy-MM-dd HH:mm:ss}). This would result in negative times. Please fix Race.StartTime.";
                        _logger.LogError(
                            "Race.StartTime appears to be in the future for Race {RaceId}. StartTime: {StartTime}, Earliest reading: {EarliestReading}",
                            decryptedRaceId, raceStartTime.Value, earliestReading);
                        response.Status = "Failed";
                        return response;
                    }
                }

                // **MERGE CHILD TO PARENT**: Map child checkpoint readings to parent checkpoint
                // This ensures readings from child devices are grouped with parent device readings
                var readingsWithMergedCheckpoints = rawReadings.Select(r => new
                {
                    r.Reading,
                    // If this checkpoint is a child, use the parent checkpoint ID instead
                    CheckpointId = childToParentCheckpointMap.TryGetValue(r.CheckpointId, out var parentId)
                        ? parentId
                        : r.CheckpointId,
                    OriginalCheckpointId = r.CheckpointId,
                    r.ParticipantId,
                    r.RawReadId
                }).ToList();

                // Log how many readings were remapped
                var remappedCount = readingsWithMergedCheckpoints.Count(r => r.CheckpointId != r.OriginalCheckpointId);
                if (remappedCount > 0)
                {
                    _logger.LogInformation(
                        "Remapped {Count} readings from child checkpoints to parent checkpoints for merging",
                        remappedCount);
                }

                // Group by Participant + (Merged) Checkpoint - this now merges parent and child device readings
                var grouped = readingsWithMergedCheckpoints
                    .GroupBy(r => new { r.ParticipantId, r.CheckpointId })
                    .ToList();

                response.CheckpointsProcessed = grouped.Select(g => g.Key.CheckpointId).Distinct().Count();
                response.ParticipantsProcessed = grouped.Select(g => g.Key.ParticipantId).Distinct().Count();

                // Log checkpoint processing summary
                var checkpointStats = grouped
                    .GroupBy(g => g.Key.CheckpointId)
                    .Select(g => new { CheckpointId = g.Key, ReadingCount = g.Sum(x => x.Count()) })
                    .ToList();

                foreach (var stat in checkpointStats)
                {
                    _logger.LogInformation(
                        "Checkpoint {CheckpointId}: {Count} readings from {Participants} participants",
                        stat.CheckpointId, stat.ReadingCount,
                        grouped.Where(g => g.Key.CheckpointId == stat.CheckpointId)
                               .Select(g => g.Key.ParticipantId).Distinct().Count());
                }

                // =====================================================================
                // FIX #2: Build participant start times dictionary for NetTime calculation
                // =====================================================================
                var participantStartTimes = new Dictionary<int, DateTime>();

                // Collect all start checkpoint readings (use LATEST time at start mat per participant)
                var startCheckpointReadings = readingsWithMergedCheckpoints
                    .Where(r => r.CheckpointId == startCheckpointId)
                    .GroupBy(r => r.ParticipantId)
                    .Select(g => new
                    {
                        ParticipantId = g.Key,
                        StartTime = g.Max(r => r.Reading.ReadTimeUtc) // LATEST reading at start mat
                    })
                    .ToList();

                foreach (var startReading in startCheckpointReadings)
                {
                    participantStartTimes[startReading.ParticipantId] = startReading.StartTime;
                }

                _logger.LogInformation(
                    "Built participant start times dictionary: {Count} participants have start checkpoint readings",
                    participantStartTimes.Count);

                // Process all groups in parallel using LINQ - no for loop needed
                var normalizedReadings = grouped.Select(group =>
                {
                    // ==========================================================
                    // FIX: For START checkpoint, pick LAST entry (runner leaving mat).
                    //      For all other checkpoints, pick EARLIEST entry.
                    // ==========================================================
                    var isStartCheckpoint = group.Key.CheckpointId == startCheckpointId;

                    var bestReading = isStartCheckpoint
                        ? group
                            .OrderByDescending(r => r.Reading.TimestampMs)  // LAST entry for start
                            .ThenByDescending(r => r.Reading.RssiDbm ?? decimal.MinValue)
                            .First()
                        : group
                            .OrderBy(r => r.Reading.TimestampMs)             // EARLIEST entry for others
                            .ThenByDescending(r => r.Reading.RssiDbm ?? decimal.MinValue)
                            .First();

                    if (isStartCheckpoint && group.Count() > 1)
                    {
                        _logger.LogDebug(
                            "Start checkpoint: Participant {ParticipantId} had {Count} readings, picked LAST at {Time} " +
                            "(earliest was {EarliestTime})",
                            group.Key.ParticipantId, group.Count(),
                            bestReading.Reading.ReadTimeUtc.ToString("HH:mm:ss.fff"),
                            group.OrderBy(r => r.Reading.TimestampMs).First().Reading.ReadTimeUtc.ToString("HH:mm:ss.fff"));
                    }

                    // Log if there are multiple readings for same participant/checkpoint (duplicates or parent-child merged)
                    if (group.Count() > 1)
                    {
                        var timeSpread = group.Max(r => r.Reading.TimestampMs) - group.Min(r => r.Reading.TimestampMs);
                        var timeSpreadSeconds = timeSpread / 1000.0;

                        // Check if readings came from different checkpoints (parent-child merge)
                        var distinctOriginalCheckpoints = group.Select(r => r.OriginalCheckpointId).Distinct().Count();
                        var mergeInfo = distinctOriginalCheckpoints > 1
                            ? $"(merged from {distinctOriginalCheckpoints} devices) "
                            : "";

                        _logger.LogDebug(
                            "Participant {ParticipantId} at Checkpoint {CheckpointId}: {Count} readings {MergeInfo}over {Seconds:F1}s. " +
                            "Using {Strategy} reading at {Time}",
                            group.Key.ParticipantId, group.Key.CheckpointId, group.Count(), mergeInfo, timeSpreadSeconds,
                            isStartCheckpoint ? "latest" : "earliest",
                            bestReading.Reading.ReadTimeUtc.ToString("HH:mm:ss"));
                    }

                    // Calculate GunTime (milliseconds from race start)
                    long? gunTime = null;
                    if (raceStartTime.HasValue)
                    {
                        gunTime = (long)(bestReading.Reading.ReadTimeUtc - raceStartTime.Value).TotalMilliseconds;
                    }

                    // =====================================================================
                    // FIX #2: Calculate NetTime (milliseconds from participant start)
                    // =====================================================================
                    long? netTime = null;

                    if (isStartCheckpoint)
                    {
                        // Special case: At start checkpoint, NetTime equals GunTime
                        // (participant's start time is their first chip crossing)
                        netTime = gunTime;
                    }
                    else if (participantStartTimes.TryGetValue(bestReading.ParticipantId, out var participantStart))
                    {
                        // Calculate NetTime from participant's start crossing
                        netTime = (long)(bestReading.Reading.ReadTimeUtc - participantStart).TotalMilliseconds;

                        // Validate NetTime is not negative (would indicate start assignment error)
                        if (netTime < 0)
                        {
                            _logger.LogWarning(
                                "Participant {ParticipantId} has negative NetTime ({NetTime}ms) at checkpoint {CheckpointId}. " +
                                "Reading time {ReadTime} is before participant start {StartTime}. Setting NetTime to null.",
                                bestReading.ParticipantId, netTime, group.Key.CheckpointId,
                                bestReading.Reading.ReadTimeUtc.ToString("HH:mm:ss"),
                                participantStart.ToString("HH:mm:ss"));
                            netTime = null;
                        }
                    }
                    else
                    {
                        // Participant has no start reading - log warning
                        _logger.LogWarning(
                            "Participant {ParticipantId} has no start checkpoint reading. NetTime will be null for checkpoint {CheckpointId}.",
                            bestReading.ParticipantId, group.Key.CheckpointId);
                    }

                    // Create normalized reading - use group.Key.CheckpointId which is the parent checkpoint
                    // (after merging child to parent), not bestReading.CheckpointId
                    return new ReadNormalized
                    {
                        EventId = decryptedEventId,
                        ParticipantId = bestReading.ParticipantId,
                        CheckpointId = group.Key.CheckpointId, // Use merged (parent) checkpoint ID
                        RawReadId = bestReading.Reading.Id,
                        ChipTime = bestReading.Reading.ReadTimeUtc,
                        GunTime = gunTime,
                        NetTime = netTime, // Now properly calculated!
                        IsManualEntry = false,
                        CreatedByUserId = userId,
                        AuditProperties = new Models.Data.Common.AuditProperties
                        {
                            CreatedBy = userId,
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };
                }).ToList();

                var duplicateCount = rawReadings.Count - normalizedReadings.Count;

                // =====================================================================
                // FIX: Validate monotonically increasing checkpoint times per participant
                // =====================================================================
                var checkpointOrder = allCheckpoints
                    .OrderBy(cp => cp.DistanceFromStart)
                    .Select((cp, idx) => new { cp.Id, Order = idx, cp.DistanceFromStart })
                    .ToDictionary(x => x.Id, x => x);

                // Group normalized readings by participant and validate ordering
                var readingsByParticipantForValidation = normalizedReadings
                    .GroupBy(nr => nr.ParticipantId)
                    .ToList();

                var invalidReadings = new List<ReadNormalized>();

                foreach (var participantGroup in readingsByParticipantForValidation)
                {
                    var orderedReadings = participantGroup
                        .Where(nr => checkpointOrder.ContainsKey(nr.CheckpointId))
                        .OrderBy(nr => checkpointOrder[nr.CheckpointId].Order)
                        .ToList();

                    // Check each consecutive pair
                    for (int i = 1; i < orderedReadings.Count; i++)
                    {
                        var prev = orderedReadings[i - 1];
                        var curr = orderedReadings[i];

                        if (curr.ChipTime <= prev.ChipTime)
                        {
                            _logger.LogWarning(
                                "MONOTONIC VIOLATION: Participant {ParticipantId} - " +
                                "Checkpoint {PrevCp} ({PrevDist}km) at {PrevTime} >= " +
                                "Checkpoint {CurrCp} ({CurrDist}km) at {CurrTime}. " +
                                "Flagging reading for review.",
                                participantGroup.Key,
                                prev.CheckpointId,
                                checkpointOrder[prev.CheckpointId].DistanceFromStart,
                                prev.ChipTime.ToString("HH:mm:ss"),
                                curr.CheckpointId,
                                checkpointOrder[curr.CheckpointId].DistanceFromStart,
                                curr.ChipTime.ToString("HH:mm:ss"));

                            // Remove the invalid reading (the one that violates monotonic order)
                            invalidReadings.Add(curr);
                        }
                    }
                }

                if (invalidReadings.Count > 0)
                {
                    _logger.LogWarning(
                        "Removed {Count} normalized readings that violated monotonic time ordering",
                        invalidReadings.Count);
                    normalizedReadings = normalizedReadings
                        .Except(invalidReadings)
                        .ToList();
                }

                await _repository.BeginTransactionAsync();

                try
                {
                    // TRUE BULK INSERT - Single DB roundtrip
                    if (normalizedReadings.Count > 0)
                    {
                        await normalizedRepo.BulkInsertAsync(normalizedReadings);
                        _logger.LogInformation("Bulk inserted {Count} normalized readings", normalizedReadings.Count);
                    }

                    await _repository.CommitTransactionAsync();

                    response.NormalizedReadings = normalizedReadings.Count;
                    response.DuplicatesRemoved = duplicateCount;
                    response.Status = "Completed";

                    var endTime = DateTime.UtcNow;
                    response.ProcessingTimeMs = (long)(endTime - startTime).TotalMilliseconds;

                    _logger.LogInformation(
                        "Deduplication completed. Normalized: {Normalized}, Duplicates: {Duplicates}, " +
                        "Monotonic violations removed: {Violations}, Time: {Time}ms",
                        normalizedReadings.Count, duplicateCount, invalidReadings.Count, response.ProcessingTimeMs);

                    return response;
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during deduplication: {ex.Message}";
                _logger.LogError(ex, "Error during deduplication and normalization");
                response.Status = "Failed";
                return response;
            }
        }


        public async Task<CalculateResultsResponse> CalculateRaceResultsAsync(string eventId, string raceId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var startTime = DateTime.UtcNow;

            var response = new CalculateResultsResponse
            {
                ProcessedAt = startTime,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting race results calculation for Race {RaceId}", decryptedRaceId);

                // Get race details
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (race == null)
                {
                    ErrorMessage = "Race not found";
                    response.Status = "Failed";
                    return response;
                }

                // Get all checkpoints ordered by distance
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var checkpoints = await checkpointRepo.GetQuery(cp =>
                    cp.RaceId == decryptedRaceId &&
                    cp.EventId == decryptedEventId &&
                    cp.AuditProperties.IsActive &&
                    !cp.AuditProperties.IsDeleted)
                    .OrderBy(cp => cp.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync();

                if (checkpoints.Count == 0)
                {
                    ErrorMessage = "No checkpoints found for this race";
                    response.Status = "Failed";
                    return response;
                }

                var parentCheckpoints = checkpoints
                                        .Where(cp => !cp.ParentDeviceId.HasValue || cp.ParentDeviceId == 0)
                                        .OrderBy(cp => cp.DistanceFromStart)
                                        .ToList();
                // Identify start checkpoint (minimum distance) and finish checkpoint (maximum distance)
                var startCheckpoint = parentCheckpoints.First();
                var finishCheckpoint = parentCheckpoints.Last();

                _logger.LogInformation(
                    "Start checkpoint: {StartId} (Distance: {StartDist}), Finish checkpoint: {FinishId} (Distance: {FinishDist})",
                    startCheckpoint.Id, startCheckpoint.DistanceFromStart,
                    finishCheckpoint.Id, finishCheckpoint.DistanceFromStart);

                // Get ALL registered participants for this race
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var allParticipants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .ToListAsync();

                if (allParticipants.Count == 0)
                {
                    response.Status = "Completed";
                    response.Message = "No registered participants found.";
                    return response;
                }

                // Get normalized readings at START checkpoint — to determine DNS
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var startReadings = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.CheckpointId == startCheckpoint.Id &&
                    rn.AuditProperties.IsActive &&
                    !rn.AuditProperties.IsDeleted)
                    .ToListAsync();

                var participantsWithStart = startReadings
                    .Select(r => r.ParticipantId)
                    .ToHashSet();

                // Get normalized readings at FINISH checkpoint — to determine Finished vs DNF
                var finishReadings = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.CheckpointId == finishCheckpoint.Id &&
                    rn.AuditProperties.IsActive &&
                    !rn.AuditProperties.IsDeleted)
                    .Include(rn => rn.Participant)
                    .OrderBy(rn => rn.GunTime ?? long.MaxValue)
                    .ToListAsync();

                var participantsWithFinish = finishReadings
                    .Select(r => r.ParticipantId)
                    .ToHashSet();

                // =====================================================================
                // Validate result times before processing
                // =====================================================================
                var negativeGunTimes = finishReadings.Where(r => r.GunTime.HasValue && r.GunTime.Value < 0).ToList();
                if (negativeGunTimes.Any())
                {
                    ErrorMessage = $"Found {negativeGunTimes.Count} finish readings with negative GunTime. " +
                        "Race.StartTime is incorrectly configured. Please check Race.StartTime in database.";
                    _logger.LogError(
                        "Validation failed: {Count} participants have negative GunTime. Race.StartTime may be wrong. " +
                        "First few examples: {Examples}",
                        negativeGunTimes.Count,
                        string.Join(", ", negativeGunTimes.Take(5).Select(r =>
                            $"Participant {r.ParticipantId}: {r.GunTime}ms")));
                    response.Status = "Failed";
                    return response;
                }

                var negativeNetTimes = finishReadings.Where(r => r.NetTime.HasValue && r.NetTime.Value < 0).ToList();
                if (negativeNetTimes.Any())
                {
                    _logger.LogWarning(
                        "Found {Count} finish readings with negative NetTime. " +
                        "This indicates participant start time assignment errors: {Examples}",
                        negativeNetTimes.Count,
                        string.Join(", ", negativeNetTimes.Take(5).Select(r =>
                            $"Participant {r.ParticipantId}: {r.NetTime}ms")));
                }

                var veryLongTimes = finishReadings.Where(r =>
                    r.GunTime.HasValue && r.GunTime.Value > 24 * 60 * 60 * 1000).ToList();
                if (veryLongTimes.Any())
                {
                    _logger.LogWarning(
                        "Found {Count} finish readings with GunTime > 24 hours: {Examples}",
                        veryLongTimes.Count,
                        string.Join(", ", veryLongTimes.Take(5).Select(r =>
                            $"Participant {r.ParticipantId}: {TimeSpan.FromMilliseconds(r.GunTime.Value):hh\\:mm\\:ss}")));
                }

                // =====================================================================
                // Classify all participants: Finished, DNF, or DNS
                // =====================================================================
                var finishedParticipantIds = new HashSet<int>();
                var dnfParticipantIds = new HashSet<int>();
                var dnsParticipantIds = new HashSet<int>();

                foreach (var participant in allParticipants)
                {
                    if (participantsWithFinish.Contains(participant.Id))
                    {
                        finishedParticipantIds.Add(participant.Id);
                    }
                    else if (!participantsWithStart.Contains(participant.Id))
                    {
                        // No start reading → Did Not Start
                        dnsParticipantIds.Add(participant.Id);
                    }
                    else
                    {
                        // Has start but no finish → Did Not Finish
                        dnfParticipantIds.Add(participant.Id);
                    }
                }

                _logger.LogInformation(
                    "Participant classification: {Finished} Finished, {DNF} DNF, {DNS} DNS (out of {Total} registered)",
                    finishedParticipantIds.Count, dnfParticipantIds.Count,
                    dnsParticipantIds.Count, allParticipants.Count);

                // Get existing results to check for updates vs inserts
                var resultsRepo = _repository.GetRepository<Results>();
                var existingResults = await resultsRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId)
                    .ToDictionaryAsync(r => r.ParticipantId, r => r);

                await _repository.BeginTransactionAsync();

                try
                {
                    var resultsToAdd = new List<Results>();
                    var resultsToUpdate = new List<Results>();

                    // =====================================================================
                    // 1. Process FINISHED participants — ranked by GunTime ascending
                    // =====================================================================
                    var rankedFinishers = finishReadings
                        .Where(r => finishedParticipantIds.Contains(r.ParticipantId))
                        .OrderBy(r => r.GunTime ?? long.MaxValue)
                        .Select((reading, index) => new { reading, OverallRank = index + 1 })
                        .ToList();

                    foreach (var item in rankedFinishers)
                    {
                        var reading = item.reading;
                        if (existingResults.TryGetValue(reading.ParticipantId, out var existing))
                        {
                            existing.FinishTime = reading.GunTime;
                            existing.GunTime = reading.GunTime;
                            existing.NetTime = reading.NetTime;
                            existing.OverallRank = item.OverallRank;
                            existing.Status = "Finished";
                            existing.AuditProperties.UpdatedBy = userId;
                            existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            resultsToUpdate.Add(existing);
                        }
                        else
                        {
                            resultsToAdd.Add(new Results
                            {
                                EventId = decryptedEventId,
                                RaceId = decryptedRaceId,
                                ParticipantId = reading.ParticipantId,
                                FinishTime = reading.GunTime,
                                GunTime = reading.GunTime,
                                NetTime = reading.NetTime,
                                OverallRank = item.OverallRank,
                                Status = "Finished",
                                IsOfficial = false,
                                CertificateGenerated = false,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            });
                        }
                    }

                    // =====================================================================
                    // 2. Process DNF participants — no rank, no finish time
                    // =====================================================================
                    foreach (var participantId in dnfParticipantIds)
                    {
                        if (existingResults.TryGetValue(participantId, out var existing))
                        {
                            existing.FinishTime = null;
                            existing.GunTime = null;
                            existing.NetTime = null;
                            existing.OverallRank = null;
                            existing.GenderRank = null;
                            existing.CategoryRank = null;
                            existing.Status = "DNF";
                            existing.AuditProperties.UpdatedBy = userId;
                            existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            resultsToUpdate.Add(existing);
                        }
                        else
                        {
                            resultsToAdd.Add(new Results
                            {
                                EventId = decryptedEventId,
                                RaceId = decryptedRaceId,
                                ParticipantId = participantId,
                                FinishTime = null,
                                GunTime = null,
                                NetTime = null,
                                OverallRank = null,
                                Status = "DNF",
                                IsOfficial = false,
                                CertificateGenerated = false,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            });
                        }
                    }

                    // =====================================================================
                    // 3. Process DNS participants — no rank, no times
                    // =====================================================================
                    foreach (var participantId in dnsParticipantIds)
                    {
                        if (existingResults.TryGetValue(participantId, out var existing))
                        {
                            existing.FinishTime = null;
                            existing.GunTime = null;
                            existing.NetTime = null;
                            existing.OverallRank = null;
                            existing.GenderRank = null;
                            existing.CategoryRank = null;
                            existing.Status = "DNS";
                            existing.AuditProperties.UpdatedBy = userId;
                            existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            resultsToUpdate.Add(existing);
                        }
                        else
                        {
                            resultsToAdd.Add(new Results
                            {
                                EventId = decryptedEventId,
                                RaceId = decryptedRaceId,
                                ParticipantId = participantId,
                                FinishTime = null,
                                GunTime = null,
                                NetTime = null,
                                OverallRank = null,
                                Status = "DNS",
                                IsOfficial = false,
                                CertificateGenerated = false,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            });
                        }
                    }

                    // Bulk persist
                    if (resultsToAdd.Count > 0)
                    {
                        await resultsRepo.BulkInsertAsync(resultsToAdd);
                        _logger.LogInformation("Bulk inserted {Count} results", resultsToAdd.Count);
                    }

                    if (resultsToUpdate.Count > 0)
                    {
                        await resultsRepo.BulkUpdateAsync(resultsToUpdate);
                        _logger.LogInformation("Bulk updated {Count} results", resultsToUpdate.Count);
                    }

                    await _repository.SaveChangesAsync();

                    // Calculate gender rankings (only for Finished)
                    await CalculateGenderRankingsAsync(decryptedEventId, decryptedRaceId, userId);

                    // Calculate category rankings (only for Finished)
                    var categoriesProcessed = await CalculateCategoryRankingsAsync(decryptedEventId, decryptedRaceId, userId);

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    // Build response
                    var genderStats = finishReadings
                        .Where(r => finishedParticipantIds.Contains(r.ParticipantId))
                        .GroupBy(r => r.Participant?.Gender?.ToLower() ?? "other")
                        .ToDictionary(g => g.Key, g => g.Count());

                    response.TotalFinishers = finishedParticipantIds.Count;
                    response.ResultsCreated = resultsToAdd.Count;
                    response.ResultsUpdated = resultsToUpdate.Count;
                    response.DNFCount = dnfParticipantIds.Count;
                    response.DNSCount = dnsParticipantIds.Count;
                    response.CategoriesProcessed = categoriesProcessed;
                    response.GenderStats = new GenderBreakdown
                    {
                        MaleFinishers = genderStats.GetValueOrDefault("male", 0),
                        FemaleFinishers = genderStats.GetValueOrDefault("female", 0),
                        OtherFinishers = genderStats.GetValueOrDefault("other", 0)
                    };
                    response.Status = "Completed";
                    response.Message = $"Results: {finishedParticipantIds.Count} Finished, " +
                        $"{dnfParticipantIds.Count} DNF, {dnsParticipantIds.Count} DNS";

                    var endTime = DateTime.UtcNow;
                    response.ProcessingTimeMs = (long)(endTime - startTime).TotalMilliseconds;

                    _logger.LogInformation(
                        "Race results complete — Finished: {Finished} (ranked), DNF: {DNF}, DNS: {DNS}, " +
                        "Created: {Created}, Updated: {Updated}, Time: {Time}ms",
                        finishedParticipantIds.Count, dnfParticipantIds.Count, dnsParticipantIds.Count,
                        resultsToAdd.Count, resultsToUpdate.Count, response.ProcessingTimeMs);

                    return response;
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error calculating race results: {ex.Message}";
                _logger.LogError(ex, "Error calculating race results for Race {RaceId}", decryptedRaceId);
                response.Status = "Failed";
                return response;
            }
        }

        private async Task CalculateGenderRankingsAsync(int eventId, int raceId, int? userId)
        {
            var resultsRepo = _repository.GetRepository<Results>();

            var results = await resultsRepo.GetQuery(r =>
                r.EventId == eventId &&
                r.RaceId == raceId &&
                r.Status == "Finished")
                .Include(r => r.Participant)
                .OrderBy(r => r.GunTime ?? long.MaxValue)
                .ToListAsync();

            // Process all rankings using LINQ - no nested for loops
            var updatedResults = results
                .GroupBy(r => r.Participant?.Gender?.ToLower() ?? "other")
                .SelectMany(group => group
                    .OrderBy(r => r.GunTime ?? long.MaxValue)
                    .Select((result, index) =>
                    {
                        result.GenderRank = index + 1;
                        result.AuditProperties.UpdatedBy = userId;
                        result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        return result;
                    }))
                .ToList();

            // Bulk update - single DB roundtrip
            if (updatedResults.Count > 0)
            {
                await resultsRepo.BulkUpdateAsync(updatedResults);
                _logger.LogInformation("Bulk updated gender rankings for {Count} results", updatedResults.Count);
            }
        }

        private async Task<int> CalculateCategoryRankingsAsync(int eventId, int raceId, int? userId)
        {
            var resultsRepo = _repository.GetRepository<Results>();

            var results = await resultsRepo.GetQuery(r =>
                r.EventId == eventId &&
                r.RaceId == raceId &&
                r.Status == "Finished")
                .Include(r => r.Participant)
                .OrderBy(r => r.GunTime ?? long.MaxValue)
                .ToListAsync();

            // Process all rankings using LINQ - no nested for loops
            var categoryGroups = results
                .GroupBy(r => r.Participant?.AgeCategory ?? "Unknown")
                .ToList();

            var updatedResults = categoryGroups
                .SelectMany(group => group
                    .OrderBy(r => r.GunTime ?? long.MaxValue)
                    .Select((result, index) =>
                    {
                        result.CategoryRank = index + 1;
                        result.AuditProperties.UpdatedBy = userId;
                        result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        return result;
                    }))
                .ToList();

            // Bulk update - single DB roundtrip
            if (updatedResults.Count > 0)
            {
                await resultsRepo.BulkUpdateAsync(updatedResults);
                _logger.LogInformation("Bulk updated category rankings for {Count} results across {Categories} categories",
                    updatedResults.Count, categoryGroups.Count);
            }

            return categoryGroups.Count;
        }

        // ============================================================================
        // REPLACE ProcessAllRFIDDataAsync AND ProcessRFIDStagingDataAsync with these
        // two methods in RFIDImportService.cs
        //
        // KEY CHANGE: Instead of processing batch-by-batch, we now:
        //   1. Load ALL pending readings across ALL batches for the race
        //   2. Group by EPC (participant), order by ReadTimeUtc
        //   3. Validate only (EPC linking, RSSI check)
        //   4. Mark readings as Success/Invalid — NO checkpoint assignment here
        //   5. Phase 1.5 (AssignCheckpointsForLoopRaceAsync) handles all assignment
        //
        // OLD FLOW (broken for loop races):
        //   ProcessAllRFIDDataAsync → foreach batch → ProcessRFIDStagingDataAsync
        //     → per-batch checkpoint assignment (can't see full participant timeline)
        //
        // NEW FLOW:
        //   ProcessAllStagingDataForRaceAsync (processes everything at once)
        //     → validates all readings across all batches
        //     → simple devices: assigns checkpoint immediately
        //     → shared/loop devices: skips assignment (Phase 1.5 handles it)
        // ============================================================================


        /// <summary>
        /// Process ALL pending RFID readings for a race in one pass.
        /// Groups readings by EPC across all batches, validates them, and assigns
        /// checkpoints for simple (single-device) mappings only.
        /// 
        /// Loop race / shared device checkpoint assignment is DEFERRED to
        /// AssignCheckpointsForLoopRaceAsync (Phase 1.5), which has the full
        /// cross-batch participant timeline needed for turnaround-based assignment.
        /// </summary>
        public async Task<BulkProcessRFIDImportResponse> ProcessAllStagingDataForRaceAsync(string eventId, string raceId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

            var response = new BulkProcessRFIDImportResponse
            {
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation(
                    "═══ Phase 1: Processing ALL staging data for Race {RaceId} ═══",
                    decryptedRaceId);

                // ================================================================
                // 1. GET ALL PENDING BATCHES
                // Include both race-specific batches AND event-level batches (RaceId = NULL)
                // Event-level batches contain data that may span multiple races
                // ================================================================
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var pendingBatches = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    (b.RaceId == decryptedRaceId || b.RaceId == null) &&  // Include event-level batches
                    (b.Status == "uploaded" || b.Status == "uploading") &&
                    b.AuditProperties.IsActive &&
                    !b.AuditProperties.IsDeleted)
                    .OrderBy(b => b.AuditProperties.CreatedDate)
                    .ToListAsync();

                if (pendingBatches.Count == 0)
                {
                    response.Status = "NoDataToProcess";
                    response.Message = "No pending batches found for this race";
                    _logger.LogInformation("No pending batches found for Race {RaceId}", decryptedRaceId);
                    return response;
                }

                var eventLevelBatchCount = pendingBatches.Count(b => b.RaceId == null);
                var raceLevelBatchCount = pendingBatches.Count(b => b.RaceId == decryptedRaceId);

                var batchIds = pendingBatches.Select(b => b.Id).ToList();
                response.TotalBatches = pendingBatches.Count;

                _logger.LogInformation(
                    "Found {Count} pending batches ({RaceLevel} race-specific, {EventLevel} event-level): [{BatchIds}]",
                    pendingBatches.Count,
                    raceLevelBatchCount,
                    eventLevelBatchCount,
                    string.Join(", ", pendingBatches.Select(b => $"{b.Id}({b.DeviceId})")));

                // ================================================================
                // 2. LOAD ALL PENDING READINGS ACROSS ALL BATCHES
                // ================================================================
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                var allReadings = await readingRepo.GetQuery(r =>
                    batchIds.Contains(r.BatchId) &&
                    r.ProcessResult == "Pending" &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .OrderBy(r => r.Epc)
                    .ThenBy(r => r.ReadTimeUtc)
                    .ToListAsync();

                if (allReadings.Count == 0)
                {
                    response.Status = "Completed";
                    response.Message = "No pending readings to process";
                    _logger.LogInformation("No pending readings across {Count} batches", pendingBatches.Count);

                    // Mark ONLY race-specific batches as completed
                    // Event-level batches (RaceId = NULL) must remain available for other races
                    var raceLevelBatchesToUpdate = pendingBatches.Where(b => b.RaceId == decryptedRaceId).ToList();
                    foreach (var batch in raceLevelBatchesToUpdate)
                    {
                        batch.Status = "completed";
                        batch.ProcessingCompletedAt = DateTime.UtcNow;
                    }
                    if (raceLevelBatchesToUpdate.Count > 0)
                    {
                        await batchRepo.UpdateRangeAsync(raceLevelBatchesToUpdate);
                    }
                    await _repository.SaveChangesAsync();
                    return response;
                }

                _logger.LogInformation(
                    "Loaded {TotalReadings} pending readings across {BatchCount} batches. " +
                    "Unique EPCs: {UniqueEpcs}",
                    allReadings.Count,
                    batchIds.Count,
                    allReadings.Select(r => r.Epc).Distinct().Count());

                // ================================================================
                // 3. LOAD PARTICIPANT / CHIP ASSIGNMENTS
                // ================================================================
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                var chipAssignments = await chipAssignmentRepo.GetQuery(ca =>
                    ca.Participant.RaceId == decryptedRaceId &&
                    ca.Participant.EventId == decryptedEventId &&
                    !ca.UnassignedAt.HasValue &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted)
                    .Include(ca => ca.Chip)
                    .Include(ca => ca.Participant)
                    .AsNoTracking()
                    .ToListAsync();

                // EPC → Participant lookup
                var epcToParticipant = chipAssignments
                    .Where(ca => ca.Chip != null && !string.IsNullOrEmpty(ca.Chip.EPC))
                    .ToDictionary(ca => ca.Chip.EPC, ca => ca.Participant);

                _logger.LogInformation("Loaded {Count} EPC→Participant mappings", epcToParticipant.Count);

                // ================================================================
                // 4. LOAD DEVICE → CHECKPOINT MAPPINGS
                //    Determine which devices are "simple" (1 checkpoint) vs "shared" (2+ checkpoints)
                // ================================================================
                var deviceRepo = _repository.GetRepository<Device>();
                var devices = await deviceRepo.GetQuery(d =>
                    d.AuditProperties.IsActive && !d.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var deviceSerialToId = devices
                    .Where(d => !string.IsNullOrEmpty(d.DeviceId))
                    .ToDictionary(d => d.DeviceId!, d => d.Id);

                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var allCheckpoints = await checkpointRepo.GetQuery(cp =>
                    cp.RaceId == decryptedRaceId &&
                    cp.EventId == decryptedEventId &&
                    cp.AuditProperties.IsActive &&
                    !cp.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                // Group checkpoints by device to identify simple vs shared
                var checkpointsByDeviceId = allCheckpoints
                    .Where(cp => cp.DeviceId > 0)
                    .GroupBy(cp => cp.DeviceId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Simple devices: exactly 1 checkpoint mapping → can assign immediately
                var simpleDeviceToCheckpoint = checkpointsByDeviceId
                    .Where(kvp => kvp.Value.Count == 1)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value[0].Id);

                // Shared devices: 2+ checkpoint mappings → Phase 1.5 will handle
                var sharedDeviceIds = checkpointsByDeviceId
                    .Where(kvp => kvp.Value.Count > 1)
                    .Select(kvp => kvp.Key)
                    .ToHashSet();

                _logger.LogInformation(
                    "Device mapping: {Simple} simple devices (assign now), {Shared} shared devices (defer to Phase 1.5)",
                    simpleDeviceToCheckpoint.Count, sharedDeviceIds.Count);

                // Build batch → device ID (primary key) lookup
                var batchToDeviceId = new Dictionary<int, int>();
                foreach (var batch in pendingBatches)
                {
                    if (!string.IsNullOrEmpty(batch.DeviceId) &&
                        deviceSerialToId.TryGetValue(batch.DeviceId, out var deviceId))
                    {
                        batchToDeviceId[batch.Id] = deviceId;
                    }
                }

                // ================================================================
                // 5. VALIDATE ALL READINGS + ASSIGN SIMPLE CHECKPOINTS
                //    Group by EPC → order by ReadTimeUtc → validate → assign (simple only)
                // ================================================================
                var readingsToUpdate = new List<RawRFIDReading>();
                var assignmentsToAdd = new List<ReadingCheckpointAssignment>();
                var unlinkedEPCs = new HashSet<string>();

                int successCount = 0;
                int errorCount = 0;
                int simpleAssignments = 0;
                int deferredAssignments = 0;
                int noCheckpointCount = 0;

                // Get existing assignments to avoid duplicates
                var existingAssignmentReadingIds = await _repository.GetRepository<ReadingCheckpointAssignment>()
                    .GetQuery(a => a.AuditProperties.IsActive && !a.AuditProperties.IsDeleted)
                    .Select(a => a.ReadingId)
                    .ToListAsync();
                var existingAssignmentSet = new HashSet<long>(existingAssignmentReadingIds);

                // Group by EPC and process all readings for each participant together
                var readingsByEpc = allReadings
                    .GroupBy(r => r.Epc)
                    .ToList();

                _logger.LogInformation(
                    "Processing {EpcCount} unique EPCs ({TotalReadings} readings)...",
                    readingsByEpc.Count, allReadings.Count);

                await _repository.BeginTransactionAsync();

                try
                {
                    foreach (var epcGroup in readingsByEpc)
                    {
                        var epc = epcGroup.Key;
                        // Readings are already ordered by ReadTimeUtc from the query
                        var participantReadings = epcGroup.ToList();

                        // ── Link to participant ──
                        if (!epcToParticipant.TryGetValue(epc, out var participant))
                        {
                            // Unlinked EPC — belongs to another race in this event.
                            // Leave as "Pending" so the other race can process it.
                            // Do NOT mark as error or update the reading.
                            unlinkedEPCs.Add(epc);
                            // Don't add to readingsToUpdate — leave completely untouched
                            // Don't increment errorCount — these aren't errors
                            continue;
                        }

                        // ── Process each reading for this participant (in chronological order) ──
                        foreach (var reading in participantReadings)
                        {
                            // RSSI validation (relaxed to -80 dBm to capture weak but valid reads
                            // from devices like Box 16 which often report -76 dBm at the edges)
                            if (reading.RssiDbm.HasValue && reading.RssiDbm.Value < -80)
                            {
                                reading.ProcessResult = "Invalid";
                                reading.Notes = "Weak signal (RSSI < -80 dBm)";
                                reading.ProcessedAt = DateTime.UtcNow;
                                readingsToUpdate.Add(reading);
                                errorCount++;
                                continue;
                            }

                            // Mark as Success
                            reading.ProcessResult = "Success";
                            reading.Notes = $"Processed for race - {raceId}";
                            reading.ProcessedAt = DateTime.UtcNow;
                            readingsToUpdate.Add(reading);
                            successCount++;

                            // ── Checkpoint Assignment ──
                            // Only assign for SIMPLE devices (1:1 device→checkpoint mapping)
                            // Shared/loop devices are deferred to Phase 1.5
                            if (existingAssignmentSet.Contains(reading.Id))
                                continue; // Already assigned

                            var deviceId = batchToDeviceId.TryGetValue(reading.BatchId, out var dId) ? dId : 0;

                            if (deviceId > 0 && simpleDeviceToCheckpoint.TryGetValue(deviceId, out var checkpointId))
                            {
                                // Simple device: assign immediately
                                assignmentsToAdd.Add(new ReadingCheckpointAssignment
                                {
                                    ReadingId = reading.Id,
                                    CheckpointId = checkpointId,
                                    AuditProperties = new Models.Data.Common.AuditProperties
                                    {
                                        CreatedBy = userId,
                                        CreatedDate = DateTime.UtcNow,
                                        IsActive = true,
                                        IsDeleted = false
                                    }
                                });
                                reading.AssignmentMethod = "DeviceMapping";
                                simpleAssignments++;
                            }
                            else if (deviceId > 0 && sharedDeviceIds.Contains(deviceId))
                            {
                                // Shared device: defer to Phase 1.5
                                // Do NOT assign checkpoint here — turnaround-based algorithm needs
                                // the full participant timeline across all batches
                                reading.AssignmentMethod = null; // Will be set by Phase 1.5
                                deferredAssignments++;
                            }
                            else
                            {
                                // Unknown device or no mapping
                                noCheckpointCount++;
                                _logger.LogDebug(
                                    "Reading {ReadingId} (EPC {Epc}): No checkpoint mapping for device {DeviceId}",
                                    reading.Id, epc, deviceId);
                            }
                        }
                    }

                    // ================================================================
                    // 6. BULK PERSIST
                    // ================================================================
                    if (assignmentsToAdd.Count > 0)
                    {
                        var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                        await assignmentRepo.BulkInsertAsync(assignmentsToAdd);
                        _logger.LogInformation("Bulk inserted {Count} simple checkpoint assignments", assignmentsToAdd.Count);
                    }

                    if (readingsToUpdate.Count > 0)
                    {
                        await readingRepo.BulkUpdateAsync(readingsToUpdate);
                        _logger.LogInformation("Bulk updated {Count} readings", readingsToUpdate.Count);
                    }

                    // ================================================================
                    // 7. UPDATE BATCH STATUSES
                    //    Only mark RACE-LEVEL batches as completed.
                    //    Event-level batches (RaceId = NULL) must remain "uploaded" so they
                    //    can be processed for OTHER races in the same event.
                    // ================================================================
                    var raceLevelBatches = pendingBatches.Where(b => b.RaceId == decryptedRaceId).ToList();
                    var eventLevelBatches = pendingBatches.Where(b => b.RaceId == null).ToList();

                    foreach (var batch in raceLevelBatches)
                    {
                        batch.Status = "completed";
                        batch.ProcessingStartedAt ??= DateTime.UtcNow;
                        batch.ProcessingCompletedAt = DateTime.UtcNow;
                    }

                    // Event-level batches: just update timestamps, keep status as "uploaded"
                    foreach (var batch in eventLevelBatches)
                    {
                        batch.ProcessingStartedAt ??= DateTime.UtcNow;
                        // Note: NOT setting ProcessingCompletedAt or changing Status
                        // These batches need to remain available for other races
                    }

                    await batchRepo.UpdateRangeAsync(pendingBatches);

                    if (eventLevelBatches.Count > 0)
                    {
                        _logger.LogInformation(
                            "Preserved {Count} event-level batches (RaceId=NULL) for other races in this event",
                            eventLevelBatches.Count);
                    }

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    // ================================================================
                    // 8. BUILD RESPONSE
                    // ================================================================
                    response.TotalBatches = pendingBatches.Count;
                    response.SuccessfulBatches = pendingBatches.Count;
                    response.FailedBatches = 0;
                    response.TotalProcessedReadings = successCount;
                    response.Status = errorCount > 0 ? "CompletedWithErrors" : "Completed";
                    response.Message = string.Format(
                        "Processed {0} readings across {1} batches. " +
                        "Simple assignments: {2}, Deferred to Phase 1.5: {3}, Unlinked EPCs: {4}",
                        allReadings.Count, pendingBatches.Count,
                        simpleAssignments, deferredAssignments, unlinkedEPCs.Count);

                    // Per-batch results for the response
                    var readingCountByBatch = allReadings
                        .GroupBy(r => r.BatchId)
                        .ToDictionary(g => g.Key, g => g.Count());

                    foreach (var batch in pendingBatches)
                    {
                        var batchReadingCount = readingCountByBatch.GetValueOrDefault(batch.Id, 0);
                        response.BatchResults.Add(new BatchProcessResult
                        {
                            BatchId = _encryptionService.Encrypt(batch.Id.ToString()),
                            FileName = batch.OriginalFileName,
                            DeviceId = batch.DeviceId,
                            Status = "Completed",
                            SuccessCount = batchReadingCount
                        });
                    }

                    _logger.LogInformation(
                        "═══ Phase 1 COMPLETE ═══ " +
                        "Total={Total}, Success={Success}, Errors={Errors}, " +
                        "SimpleAssign={Simple}, DeferredToPhase15={Deferred}, " +
                        "UnlinkedEPCs={Unlinked}, NoMapping={NoMap}",
                        allReadings.Count, successCount, errorCount,
                        simpleAssignments, deferredAssignments,
                        unlinkedEPCs.Count, noCheckpointCount);

                    return response;
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during bulk processing: {ex.Message}";
                _logger.LogError(ex, "Error processing all staging data for Race {RaceId}", decryptedRaceId);
                response.Status = "Failed";
                response.Message = ex.Message;
                return response;
            }
        }


        /// <summary>
        /// Clears all processed data for a race, optionally keeping raw uploads.
        /// This is useful when checkpoint mappings change or data needs complete recalculation.
        /// </summary>
        public async Task<ClearDataResponse> ClearProcessedDataAsync(string eventId, string raceId, bool keepUploads = true)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

            var response = new ClearDataResponse
            {
                Status = "Processing"
            };

            await _repository.BeginTransactionAsync();

            try
            {
                _logger.LogInformation(
                    "Starting data cleanup for Event {EventId}, Race {RaceId}. KeepUploads: {KeepUploads}",
                    decryptedEventId, decryptedRaceId, keepUploads);

                // 1. Delete Results
                var resultsRepo = _repository.GetRepository<Results>();
                var results = await resultsRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId)
                    .ToListAsync();

                if (results.Count > 0)
                {
                    await resultsRepo.BulkDeleteAsync(results);
                    response.ResultsCleared = results.Count;
                    _logger.LogInformation("Cleared {Count} results", results.Count);
                }

                // 2. Delete ReadNormalized
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var normalized = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.Participant.RaceId == decryptedRaceId)
                    .ToListAsync();

                if (normalized.Count > 0)
                {
                    await normalizedRepo.BulkDeleteAsync(normalized);
                    response.NormalizedReadingsCleared = normalized.Count;
                    _logger.LogInformation("Cleared {Count} normalized readings", normalized.Count);
                }

                // 3. Get batch IDs for this race (including event-level batches)
                // For event-level batches, we need to handle their readings that belong to this race's participants
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var batches = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    (b.RaceId == decryptedRaceId || b.RaceId == null))  // Include event-level batches
                    .ToListAsync();

                var batchIds = batches.Select(b => b.Id).ToList();
                var eventLevelBatchIds = batches.Where(b => b.RaceId == null).Select(b => b.Id).ToHashSet();
                var raceLevelBatchIds = batches.Where(b => b.RaceId == decryptedRaceId).Select(b => b.Id).ToHashSet();

                // 4. Delete ReadingCheckpointAssignment
                var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                var readingRepo = _repository.GetRepository<RawRFIDReading>();

                var readingIds = await readingRepo.GetQuery(r => batchIds.Contains(r.BatchId))
                    .Select(r => r.Id)
                    .ToListAsync();

                var assignments = await assignmentRepo.GetQuery(a => readingIds.Contains(a.ReadingId))
                    .ToListAsync();

                if (assignments.Count > 0)
                {
                    await assignmentRepo.BulkDeleteAsync(assignments);
                    response.AssignmentsCleared = assignments.Count;
                    _logger.LogInformation("Cleared {Count} checkpoint assignments", assignments.Count);
                }

                // 5. Reset RawRFIDReading status (or delete if not keeping uploads)
                var readings = await readingRepo.GetQuery(r => batchIds.Contains(r.BatchId))
                    .ToListAsync();

                if (!keepUploads)
                {
                    // 5a. Delete readings directly if not keeping uploads
                    if (readings.Count > 0)
                    {
                        // Use the already-tracked entities to avoid re-querying
                        await readingRepo.BulkDeleteAsync(readings);
                        response.UploadsDeleted = readings.Count;
                        _logger.LogInformation("Deleted {Count} raw readings", readings.Count);
                    }
                }
                else
                {
                    // 5b. Reset readings to Pending if keeping uploads
                    if (readings.Count > 0)
                    {
                        foreach (var reading in readings)
                        {
                            reading.ProcessResult = "Pending";
                            reading.ProcessedAt = null;
                            reading.AssignmentMethod = null;
                            reading.Notes = null;
                        }
                        await readingRepo.UpdateRangeAsync(readings);
                        response.ReadingsReset = readings.Count;
                        _logger.LogInformation("Reset {Count} raw readings to Pending", readings.Count);
                    }
                }

                // 6. Reset or delete UploadBatch status
                // NOTE: Only affect race-specific batches, NOT event-level batches
                // Event-level batches should remain available for other races
                var raceLevelBatches = batches.Where(b => b.RaceId == decryptedRaceId).ToList();

                if (!keepUploads)
                {
                    // 6a. Delete batches if not keeping uploads (race-level only)
                    if (raceLevelBatches.Count > 0)
                    {
                        await batchRepo.BulkDeleteAsync(raceLevelBatches);
                        response.UploadsDeleted = raceLevelBatches.Count;
                        _logger.LogInformation("Deleted {Count} race-level upload batches", raceLevelBatches.Count);
                    }
                    if (eventLevelBatchIds.Count > 0)
                    {
                        _logger.LogInformation(
                            "Preserved {Count} event-level batches (shared across races)",
                            eventLevelBatchIds.Count);
                    }
                }
                else
                {
                    // 6b. Reset batches if keeping uploads (race-level only)
                    if (raceLevelBatches.Count > 0)
                    {
                        foreach (var batch in raceLevelBatches)
                        {
                            batch.Status = "uploaded";
                            batch.ProcessingStartedAt = null;
                            batch.ProcessingCompletedAt = null;
                        }
                        await batchRepo.UpdateRangeAsync(raceLevelBatches);
                        response.BatchesReset = raceLevelBatches.Count;
                        _logger.LogInformation("Reset {Count} race-level batches to uploaded status", raceLevelBatches.Count);
                    }
                    if (eventLevelBatchIds.Count > 0)
                    {
                        _logger.LogInformation(
                            "Event-level batches ({Count}) not reset - they serve multiple races",
                            eventLevelBatchIds.Count);
                    }
                }

                await _repository.SaveChangesAsync();
                await _repository.CommitTransactionAsync();

                response.Status = "Success";
                response.Message = keepUploads
                    ? "Cleared processed data. Upload batches preserved and ready for reprocessing."
                    : "Cleared all data including uploads. Race is now empty.";

                _logger.LogInformation(
                    "Data cleanup completed for Event {EventId}, Race {RaceId}. {Summary}",
                    decryptedEventId, decryptedRaceId, response.Summary);

                return response;
            }
            catch (Exception ex)
            {
                await _repository.RollbackTransactionAsync();
                ErrorMessage = $"Error clearing data: {ex.Message}";
                _logger.LogError(ex, "Error clearing processed data for Event {EventId}, Race {RaceId}",
                    decryptedEventId, decryptedRaceId);
                response.Status = "Failed";
                response.Message = ErrorMessage;
                return response;
            }
        }

        /// <summary>
        /// Reprocesses specific participants after manual corrections.
        /// Clears their processed data and recalculates from raw readings.
        /// </summary>
        public async Task<ReprocessParticipantsResponse> ReprocessParticipantsAsync(string eventId, string raceId, string[] participantIds)
        {
            var startTime = DateTime.UtcNow;
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

            var decryptedParticipantIds = participantIds
                .Select(id => Convert.ToInt32(_encryptionService.Decrypt(id)))
                .ToList();

            var response = new ReprocessParticipantsResponse
            {
                Status = "Processing",
                TotalParticipantsRequested = decryptedParticipantIds.Count
            };

            await _repository.BeginTransactionAsync();

            try
            {
                _logger.LogInformation(
                    "Starting participant reprocessing for {Count} participants in Race {RaceId}",
                    decryptedParticipantIds.Count, decryptedRaceId);

                // Verify participants exist
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participants = await participantRepo.GetQuery(p =>
                    decryptedParticipantIds.Contains(p.Id) &&
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId)
                    .ToListAsync();

                response.ParticipantsCleared = participants.Count;

                if (participants.Count < decryptedParticipantIds.Count)
                {
                    var foundIds = participants.Select(p => p.Id).ToHashSet();
                    var notFound = decryptedParticipantIds.Where(id => !foundIds.Contains(id)).ToList();
                    response.NotFoundParticipants = notFound.Select(id =>
                        _encryptionService.Encrypt(id.ToString())).ToList();

                    _logger.LogWarning("Could not find {Count} requested participants", notFound.Count);
                }

                if (participants.Count == 0)
                {
                    response.Status = "Failed";
                    response.Message = "No valid participants found to reprocess";
                    await _repository.RollbackTransactionAsync();
                    return response;
                }

                var validParticipantIds = participants.Select(p => p.Id).ToList();

                // 1. Delete their Results
                var resultsRepo = _repository.GetRepository<Results>();
                var results = await resultsRepo.GetQuery(r => validParticipantIds.Contains(r.ParticipantId))
                    .ToListAsync();

                if (results.Count > 0)
                {
                    await resultsRepo.DeleteRangeAsync(results.Select(r => r.Id).ToList());
                    response.ResultsCleared = results.Count;
                }

                // 2. Delete their ReadNormalized
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var normalized = await normalizedRepo.GetQuery(rn =>
                    validParticipantIds.Contains(rn.ParticipantId))
                    .ToListAsync();

                var readingIds = normalized.Where(n => n.RawReadId.HasValue)
                    .Select(n => n.RawReadId!.Value)
                    .ToList();

                if (normalized.Count > 0)
                {
                    await normalizedRepo.DeleteRangeAsync(normalized.Select(n => n.Id).ToList());
                    response.ReadingsCleared = normalized.Count;
                }

                // 3. Reset their RawRFIDReading (if we have the IDs)
                if (readingIds.Count > 0)
                {
                    var readingRepo = _repository.GetRepository<RawRFIDReading>();
                    var readings = await readingRepo.GetQuery(r => readingIds.Contains(r.Id))
                        .ToListAsync();

                    foreach (var reading in readings)
                    {
                        reading.ProcessResult = "Pending";
                        reading.ProcessedAt = null;
                    }
                    await readingRepo.UpdateRangeAsync(readings);
                }

                await _repository.SaveChangesAsync();
                await _repository.CommitTransactionAsync();

                // Now reprocess using the complete workflow
                _logger.LogInformation("Reprocessing {Count} participants...", participants.Count);

                var processResult = await ProcessCompleteWorkflowAsync(eventId, raceId);

                // CRITICAL FIX: After reprocessing, recalculate ALL rankings
                // This ensures rankings are correct across all participants
                _logger.LogInformation("Recalculating rankings for all participants after reprocessing...");

                await _repository.BeginTransactionAsync();
                try
                {
                    // Recalculate overall rankings for entire race
                    var allResults = await resultsRepo.GetQuery(r =>
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId &&
                        r.Status == "Finished")
                        .OrderBy(r => r.GunTime ?? long.MaxValue)
                        .ToListAsync();

                    var rankUpdates = allResults.Select((result, index) =>
                    {
                        result.OverallRank = index + 1;
                        result.AuditProperties.UpdatedBy = userId;
                        result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        return result;
                    }).ToList();

                    if (rankUpdates.Count > 0)
                    {
                        await resultsRepo.BulkUpdateAsync(rankUpdates);
                        _logger.LogInformation("Recalculated overall rankings for {Count} participants", rankUpdates.Count);
                    }

                    // Recalculate gender rankings for entire race
                    await CalculateGenderRankingsAsync(decryptedEventId, decryptedRaceId, userId);

                    // Recalculate category rankings for entire race
                    var categoriesProcessed = await CalculateCategoryRankingsAsync(decryptedEventId, decryptedRaceId, userId);

                    _logger.LogInformation("Recalculated rankings across {Categories} categories", categoriesProcessed);

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }

                response.Status = processResult.Status;
                response.ParticipantsReprocessed = participants.Count;
                response.ReadingsCreated = processResult.TotalNormalizedReadings;
                response.ResultsCreated = processResult.ResultsCreated + processResult.ResultsUpdated;
                response.Message = $"Successfully reprocessed {participants.Count} participants and recalculated all rankings";

                response.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation(
                    "Participant reprocessing completed in {Time}ms. Reprocessed: {Count}",
                    response.ProcessingTimeMs, participants.Count);

                return response;
            }
            catch (Exception ex)
            {
                await _repository.RollbackTransactionAsync();
                ErrorMessage = $"Error reprocessing participants: {ex.Message}";
                _logger.LogError(ex, "Error reprocessing participants");
                response.Status = "Failed";
                response.Message = ErrorMessage;
                response.Errors.Add(ex.Message);
                return response;
            }
        }

        /// <summary>
        /// Reprocesses a single upload batch after configuration changes.
        /// </summary>
        public async Task<ProcessRFIDImportResponse> ReprocessBatchAsync(string eventId, string raceId, string uploadBatchId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var decryptedBatchId = Convert.ToInt32(_encryptionService.Decrypt(uploadBatchId));

            var response = new ProcessRFIDImportResponse
            {
                ImportBatchId = decryptedBatchId,
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Reprocessing batch {BatchId}", decryptedBatchId);

                // Verify batch exists
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var batch = await batchRepo.GetQuery(b =>
                    b.Id == decryptedBatchId &&
                    b.EventId == decryptedEventId &&
                    b.RaceId == decryptedRaceId)
                    .FirstOrDefaultAsync();

                if (batch == null)
                {
                    ErrorMessage = "Upload batch not found";
                    response.Status = "Failed";
                    return response;
                }

                await _repository.BeginTransactionAsync();

                try
                {
                    // 1. Get readings for this batch
                    var readingRepo = _repository.GetRepository<RawRFIDReading>();
                    var readings = await readingRepo.GetQuery(r => r.BatchId == decryptedBatchId)
                        .ToListAsync();

                    var readingIds = readings.Select(r => r.Id).ToList();

                    // 2. Delete checkpoint assignments for these readings
                    var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                    var assignments = await assignmentRepo.GetQuery(a => readingIds.Contains(a.ReadingId))
                        .ToListAsync();

                    if (assignments.Count > 0)
                    {
                        await assignmentRepo.DeleteRangeAsync(assignments.Select(a => a.Id).ToList());
                    }

                    // 3. Delete normalized readings from these raw reads
                    var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                    var normalized = await normalizedRepo.GetQuery(rn =>
                        rn.RawReadId.HasValue && readingIds.Contains(rn.RawReadId.Value))
                        .ToListAsync();

                    if (normalized.Count > 0)
                    {
                        await normalizedRepo.DeleteRangeAsync(normalized.Select(n => n.Id).ToList());
                    }

                    // 4. Reset readings to Pending
                    foreach (var reading in readings)
                    {
                        reading.ProcessResult = "Pending";
                        reading.ProcessedAt = null;
                        reading.AssignmentMethod = null;
                        reading.Notes = null;
                    }
                    await readingRepo.UpdateRangeAsync(readings);

                    // 5. Reset batch status
                    batch.Status = "uploaded";
                    batch.ProcessingStartedAt = null;
                    batch.ProcessingCompletedAt = null;
                    await batchRepo.UpdateAsync(batch);

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    _logger.LogInformation(
                        "Cleared data for batch {BatchId}. Readings reset: {Count}",
                        decryptedBatchId, readings.Count);

                    // 6. Now reprocess using existing method
                    var processRequest = new ProcessRFIDImportRequest
                    {
                        EventId = eventId,
                        RaceId = raceId,
                        UploadBatchId = uploadBatchId
                    };

                    return await ProcessRFIDStagingDataAsync(processRequest);
                }
                catch
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error reprocessing batch: {ex.Message}";
                _logger.LogError(ex, "Error reprocessing batch {BatchId}", decryptedBatchId);
                response.Status = "Failed";
                return response;
            }
        }

        /// <summary>
        /// Assign checkpoints to readings for loop races where a single device is used at multiple checkpoints.
        /// Uses turnaround-based algorithm: readings before turnaround = outbound, after turnaround = return.
        /// Falls back to chronological order for participants without turnaround readings.
        /// </summary>
        // ============================================================================
        // REPLACE the existing AssignCheckpointsForLoopRaceAsync method in RFIDImportService.cs
        // with this implementation. Follows the 5-step algorithm using LoopRaceCheckpointAssigner.
        // ============================================================================

        /// <summary>
        /// Assign checkpoints to readings for loop races where a single device serves multiple checkpoints.
        /// 
        /// ┌─────────────────────────────────────────────────────────────────────────┐
        /// │ Step 1: Load Data (readings, checkpoints, device mappings)             │
        /// │ Step 2: Identify Turnaround + Shared Devices                          │
        /// │ Step 3: Calculate Turnaround Time per Participant + Median fallback    │
        /// │ Step 4: Assign Checkpoints (turnaround ref → chronological order)     │
        /// │ Step 5: Deduplicate (Start=LAST, Others=EARLIEST)                     │
        /// └─────────────────────────────────────────────────────────────────────────┘
        /// </summary>
        // ============================================================================
        // REPLACE the existing AssignCheckpointsForLoopRaceAsync in RFIDImportService.cs
        //
        // FIXES from real data analysis:
        //   1. CROSS-BATCH DEDUP: Same .db files uploaded multiple times → 100 rows but 21 unique
        //      readings. Without dedup, chronological rank overflows (5 copies of Start reading = rank 5)
        //   2. DEVICE ID RESOLUTION: Resolve via BOTH Device.DeviceId (MAC) AND Device.Name (friendly)
        //      Some batches use "Box 16", others "0016251292a1" — both are Device.Id=12
        //   3. STALE ASSIGNMENT CLEANUP: Checkpoints can be recreated (new IDs). Old assignments
        //      that reference deleted checkpoint IDs must be cleaned up before re-assigning
        //   4. RACE START TIME: Filter out readings before Race.StartTime
        //   5. DEDUP WINDOW: Multiple reads within N seconds on same shared group = same pass
        //   6. RE-PROCESSING: Delete existing assignments for current race checkpoints before reassigning
        // ============================================================================

        /// <summary>
        /// Phase 1.5: Assign checkpoints for loop/shared devices using turnaround-based algorithm.
        /// Processes ALL readings across ALL batches, deduplicated, with full participant timeline.
        /// </summary>
        public async Task<AssignCheckpointsResponse> AssignCheckpointsForLoopRaceAsync(string eventId, string raceId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var startTime = DateTime.UtcNow;

            // Configurable dedup window: readings within this many seconds on the same
            // shared group are treated as the same "pass" over the mat
            const int DEDUP_WINDOW_SECONDS = 30;

            var response = new AssignCheckpointsResponse
            {
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation(
                    "═══ Phase 1.5: Loop Race Checkpoint Assignment START — Race {RaceId} ═══",
                    decryptedRaceId);

                // ================================================================
                // STEP 1: LOAD DATA
                // ================================================================
                _logger.LogInformation("Step 1: Loading data...");

                // 1a. Race start time (required)
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (race == null || !race.StartTime.HasValue)
                {
                    ErrorMessage = "Race not found or missing start time";
                    response.Status = "Failed";
                    response.ErrorMessage = ErrorMessage;
                    return response;
                }

                var raceStartTime = race.StartTime.Value;

                // 1b. Devices — build resolution map for BOTH serial (MAC) AND friendly name
                var deviceRepo = _repository.GetRepository<Device>();
                var devices = await deviceRepo.GetQuery(d =>
                    d.AuditProperties.IsActive && !d.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                // FIX #2: Resolve by BOTH Device.DeviceId (MAC) and Device.Name (friendly name)
                var deviceLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var device in devices)
                {
                    if (!string.IsNullOrEmpty(device.DeviceId))
                        deviceLookup[device.DeviceId] = device.Id;    // MAC: "0016251292a1" → 12
                    if (!string.IsNullOrEmpty(device.Name))
                        deviceLookup[device.Name] = device.Id;         // Name: "Box 16" → 12
                }

                // 1c. Checkpoints
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var checkpoints = await checkpointRepo.GetQuery(cp =>
                    cp.RaceId == decryptedRaceId &&
                    cp.EventId == decryptedEventId &&
                    cp.AuditProperties.IsActive &&
                    !cp.AuditProperties.IsDeleted)
                    .OrderBy(cp => cp.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync();

                if (checkpoints.Count == 0)
                {
                    ErrorMessage = "No checkpoints found for race";
                    response.Status = "Failed";
                    response.ErrorMessage = ErrorMessage;
                    return response;
                }

                var currentCheckpointIds = checkpoints.Select(cp => cp.Id).ToHashSet();

                // 1d. EPC → Participant
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                var epcToParticipant = (await chipAssignmentRepo.GetQuery(ca =>
                    ca.Participant.RaceId == decryptedRaceId &&
                    !ca.UnassignedAt.HasValue &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted)
                    .Include(ca => ca.Chip)
                    .Select(ca => new { EPC = ca.Chip.EPC, ca.ParticipantId })
                    .ToListAsync())
                    .ToDictionary(ca => ca.EPC, ca => ca.ParticipantId);

                // 1e. Batches → device serial mapping (including event-level batches)
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var batches = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    (b.RaceId == decryptedRaceId || b.RaceId == null))  // Include event-level batches
                    .AsNoTracking()
                    .ToListAsync();

                var batchIds = batches.Select(b => b.Id).ToList();
                var batchToDeviceSerial = batches.ToDictionary(b => b.Id, b => b.DeviceId);

                // ================================================================
                // FIX #3 + #7: CLEAN UP STALE AND ALL EXISTING ASSIGNMENTS
                // Delete assignments that reference non-existent checkpoints (stale)
                // AND delete ALL existing assignments for this race's readings.
                // Phase 1.5 handles ALL device types (shared, turnaround, AND single),
                // so it must clear Phase 1's simple assignments too to avoid duplicate
                // key violations on insert (e.g. child device 4317 assigned by Phase 1
                // AND Phase 1.5 → IX_ReadingCheckpointAssignments_ReadingId_CheckpointId).
                // ================================================================
                var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();

                // FIX BUG 2: Scope to only THIS race's participant EPCs
                var raceEpcList = epcToParticipant.Keys.ToList();      // For EF Core queries
                var raceEpcSet = new HashSet<string>(raceEpcList);      // For in-memory lookups

                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                var raceReadingIds = await readingRepo.GetQuery(r =>
                    batchIds.Contains(r.BatchId) &&
                    raceEpcList.Contains(r.Epc) &&  // ← FIX: List for EF Core SQL translation
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .Select(r => r.Id)
                    .ToListAsync();
                var raceReadingIdSet = new HashSet<long>(raceReadingIds);  // For in-memory lookups

                // Find existing assignments for THIS race's readings only
                var existingAssignments = await assignmentRepo.GetQuery(a =>
                    raceReadingIds.Contains(a.ReadingId))  // ← FIX: List for EF Core SQL translation
                    .ToListAsync();

                // Delete stale assignments (referencing checkpoints that no longer exist)
                var staleAssignments = existingAssignments
                    .Where(a => !currentCheckpointIds.Contains(a.CheckpointId))
                    .ToList();

                if (staleAssignments.Count > 0)
                {
                    _logger.LogWarning(
                        "FIX #3: Found {Count} STALE assignments referencing deleted checkpoint IDs. Deleting.",
                        staleAssignments.Count);
                    await assignmentRepo.BulkDeleteAsync(staleAssignments);
                }

                // FIX #7: Delete ALL remaining existing assignments for this race's readings.
                // Phase 1 may have already assigned "simple" child device checkpoints (e.g. 4317)
                // that Phase 1.5 also assigns, causing duplicate key violations on BulkInsert.
                // Phase 1.5 re-creates everything (shared + turnaround + single device assignments).
                var staleIds = staleAssignments.Select(s => s.Id).ToHashSet();
                var remainingAssignments = existingAssignments
                    .Where(a => !staleIds.Contains(a.Id))  // Skip already-deleted stale ones
                    .ToList();

                if (remainingAssignments.Count > 0)
                {
                    _logger.LogInformation(
                        "FIX #7: Deleting {Count} existing assignments for re-processing (includes Phase 1 simple + shared device assignments)",
                        remainingAssignments.Count);
                    await assignmentRepo.BulkDeleteAsync(remainingAssignments);
                }

                // ================================================================
                // 1f. Load ALL readings (Success status) across all batches
                //     FIX #4: Filter readings after race start time
                // ================================================================
                var allReadings = await readingRepo.GetQuery(r =>
                    r.ProcessResult == "Success" &&
                    batchIds.Contains(r.BatchId) &&
                    r.ReadTimeUtc >= raceStartTime &&  // FIX #4: Only readings after race starts
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .ToListAsync();

                _logger.LogInformation(
                    "Step 1: Loaded {Checkpoints} checkpoints, {Devices} devices, " +
                    "{Participants} EPC mappings, {Readings} valid readings (after race start)",
                    checkpoints.Count, devices.Count, epcToParticipant.Count, allReadings.Count);

                if (allReadings.Count == 0)
                {
                    response.Status = "Completed";
                    response.CheckpointsAssigned = 0;
                    return response;
                }

                // ================================================================
                // FIX #1: CROSS-BATCH DEDUPLICATION
                // Same .db file uploaded multiple times creates duplicate readings.
                // Deduplicate by (Epc, TimestampMs) — keep the first occurrence.
                // ================================================================
                // FIX BUG 3: Filter to only THIS race's participant EPCs BEFORE dedup
                var raceFilteredReadings = allReadings
                    .Where(r => raceEpcSet.Contains(r.Epc))  // ← In-memory: HashSet is fine
                    .ToList();

                var filteredOut = allReadings.Count - raceFilteredReadings.Count;
                if (filteredOut > 0)
                {
                    _logger.LogInformation(
                        "EPC filter: {Before} → {After} readings (filtered out {Removed} readings from other races)",
                        allReadings.Count, raceFilteredReadings.Count, filteredOut);
                }

                var beforeDedup = raceFilteredReadings.Count;
                var dedupedReadings = raceFilteredReadings
                    .GroupBy(r => new { r.Epc, r.TimestampMs })
                    .Select(g => g.OrderBy(r => r.Id).First())  // Keep earliest DB row
                    .ToList();

                var duplicatesRemoved = beforeDedup - dedupedReadings.Count;

                if (duplicatesRemoved > 0)
                {
                    _logger.LogWarning(
                        "FIX #1: Cross-batch deduplication: {Before} → {After} readings " +
                        "(removed {Removed} duplicates from re-uploaded .db files)",
                        beforeDedup, dedupedReadings.Count, duplicatesRemoved);
                }

                // ================================================================
                // 1g. Map readings to ReadingInput with resolved DeviceId
                //     FIX #2: Use both Device.DeviceId AND Device.Name for resolution
                // ================================================================
                var readingInputs = new List<LoopRaceCheckpointAssigner.ReadingInput>();
                int unresolvedDevices = 0;

                foreach (var r in dedupedReadings)
                {
                    // Try batch device serial first, then reading's own DeviceId
                    var deviceSerial = batchToDeviceSerial.TryGetValue(r.BatchId, out var serial) ? serial : r.DeviceId;
                    int deviceId = 0;

                    // FIX #2: Try resolving via serial/MAC first, then try name
                    if (!string.IsNullOrEmpty(deviceSerial) && deviceLookup.TryGetValue(deviceSerial, out var id))
                    {
                        deviceId = id;
                    }
                    else if (!string.IsNullOrEmpty(r.DeviceId) && deviceLookup.TryGetValue(r.DeviceId, out var id2))
                    {
                        deviceId = id2;
                    }

                    if (deviceId == 0)
                    {
                        unresolvedDevices++;
                        continue;
                    }

                    readingInputs.Add(new LoopRaceCheckpointAssigner.ReadingInput
                    {
                        ReadingId = r.Id,
                        Epc = r.Epc,
                        DeviceId = deviceId,
                        DeviceSerial = deviceSerial,
                        ReadTimeUtc = r.ReadTimeUtc
                    });
                }

                if (unresolvedDevices > 0)
                {
                    _logger.LogWarning(
                        "FIX #2: {Count} readings had unresolvable device IDs (not in Device table by serial or name)",
                        unresolvedDevices);
                }

                _logger.LogInformation(
                    "After dedup + device resolution: {Count} readings ready for assignment",
                    readingInputs.Count);

                // ================================================================
                // STEP 2: IDENTIFY TURNAROUND + SHARED DEVICES
                // ================================================================
                var assigner = new LoopRaceCheckpointAssigner(_logger);

                var turnaroundConfig = assigner.IdentifyTurnaroundCheckpoint(checkpoints);
                var sharedDevices = assigner.IdentifySharedDevices(checkpoints);

                // Build single-device checkpoint lookup (non-shared, non-turnaround)
                var singleDeviceCheckpoints = checkpoints
                    .Where(cp => cp.DeviceId > 0)
                    .GroupBy(cp => cp.DeviceId)
                    .Where(g => g.Count() == 1 &&
                                (turnaroundConfig == null || g.Key != turnaroundConfig.DeviceId) &&
                                !sharedDevices.ContainsKey(g.Key))
                    .ToDictionary(g => g.Key, g => g.ToList());

                // ================================================================
                // FIX #5: DEDUP WINDOW — Group readings within N seconds as same "pass"
                // Before passing to the assigner, collapse readings from the same
                // shared group that are within DEDUP_WINDOW_SECONDS into one representative
                // ================================================================
                var deviceToGroup = new Dictionary<int, string>();
                foreach (var kvp in sharedDevices)
                {
                    deviceToGroup[kvp.Key] = kvp.Value.SharedGroupKey;
                }

                // Group by EPC, then within each EPC, collapse passes within shared groups
                var readingsByEpc = new Dictionary<string, List<LoopRaceCheckpointAssigner.ReadingInput>>();

                foreach (var epcGroup in readingInputs.GroupBy(r => r.Epc))
                {
                    var epc = epcGroup.Key;
                    var sorted = epcGroup.OrderBy(r => r.ReadTimeUtc).ToList();

                    // For each shared group, collapse readings within DEDUP_WINDOW_SECONDS
                    // into one representative reading (keep the one with best timing = earliest for the pass)
                    var collapsedReadings = new List<LoopRaceCheckpointAssigner.ReadingInput>();
                    var lastPassTimeByGroup = new Dictionary<string, DateTime>();

                    foreach (var reading in sorted)
                    {
                        if (deviceToGroup.TryGetValue(reading.DeviceId, out var groupKey))
                        {
                            // Shared device: check if this is within dedup window of last pass in this group
                            if (lastPassTimeByGroup.TryGetValue(groupKey, out var lastTime))
                            {
                                var gap = (reading.ReadTimeUtc - lastTime).TotalSeconds;
                                if (gap <= DEDUP_WINDOW_SECONDS)
                                {
                                    // Same pass — skip this reading (keep earlier one)
                                    continue;
                                }
                            }
                            // New pass
                            lastPassTimeByGroup[groupKey] = reading.ReadTimeUtc;
                            collapsedReadings.Add(reading);
                        }
                        else
                        {
                            // Non-shared device (turnaround, single-mapping): keep all readings
                            // (the assigner's Step 5 dedup will handle same-checkpoint duplicates)
                            collapsedReadings.Add(reading);
                        }
                    }

                    readingsByEpc[epc] = collapsedReadings;
                }

                var totalCollapsed = readingsByEpc.Values.Sum(v => v.Count);
                if (totalCollapsed < readingInputs.Count)
                {
                    _logger.LogInformation(
                        "FIX #5: Pass dedup window ({Window}s): {Before} → {After} readings " +
                        "(collapsed {Removed} same-pass readings within shared groups)",
                        DEDUP_WINDOW_SECONDS, readingInputs.Count, totalCollapsed,
                        readingInputs.Count - totalCollapsed);
                }

                // ================================================================
                // STEP 3: CALCULATE TURNAROUND TIMES
                // ================================================================
                var turnaroundTimes = new Dictionary<string, DateTime>();
                DateTime? medianTurnaround = null;

                if (turnaroundConfig != null)
                {
                    // FIX #8: Include child devices in turnaround calculation.
                    // Participants read only by a child device (e.g. Box 24/Device 14)
                    // and not the parent (Box 19/Device 13) were getting no turnaround time,
                    // causing their Finish readings to be misclassified as Start.
                    var turnaroundDeviceIds = new HashSet<int> { turnaroundConfig.DeviceId };
                    foreach (var cp in checkpoints.Where(c => c.ParentDeviceId == turnaroundConfig.DeviceId))
                    {
                        turnaroundDeviceIds.Add(cp.DeviceId);
                    }

                    _logger.LogInformation(
                        "FIX #8: Turnaround device IDs (parent + children): [{DeviceIds}]",
                        string.Join(", ", turnaroundDeviceIds));

                    turnaroundTimes = assigner.CalculateTurnaroundTimesPerParticipant(readingInputs, turnaroundDeviceIds);

                    medianTurnaround = assigner.CalculateMedianTurnaround(
                        turnaroundTimes, raceStartTime);
                }
                else
                {
                    _logger.LogWarning(
                        "No turnaround checkpoint found. ALL shared device assignments will use chronological fallback.");
                }

                // ================================================================
                // STEP 4: ASSIGN CHECKPOINTS
                // ================================================================
                var assignedReadings = assigner.AssignAllCheckpoints(
                    readingsByEpc,
                    turnaroundConfig,
                    sharedDevices,
                    turnaroundTimes,
                    medianTurnaround,
                    singleDeviceCheckpoints);

                // ================================================================
                // STEP 5: DEDUPLICATE
                // ================================================================
                var deduplicatedReadings = assigner.DeduplicateAssignedReadings(assignedReadings, checkpoints);

                _logger.LogInformation(
                    "Pipeline: {Deduped} deduplicated → {Collapsed} pass-collapsed → {Assigned} assigned → {Final} after final dedup",
                    dedupedReadings.Count, totalCollapsed, assignedReadings.Count, deduplicatedReadings.Count);

                // ================================================================
                // PERSIST: Create ReadingCheckpointAssignment entities
                // ================================================================
                var assignmentsToCreate = new List<ReadingCheckpointAssignment>();
                var rawReadingLookup = dedupedReadings.ToDictionary(r => r.Id);

                foreach (var assigned in deduplicatedReadings)
                {
                    assignmentsToCreate.Add(new ReadingCheckpointAssignment
                    {
                        ReadingId = assigned.ReadingId,
                        CheckpointId = assigned.CheckpointId,
                        AuditProperties = new Models.Data.Common.AuditProperties
                        {
                            CreatedBy = userId,
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    });

                    // Update raw reading with assignment method
                    if (rawReadingLookup.TryGetValue(assigned.ReadingId, out var rawReading))
                    {
                        rawReading.AssignmentMethod = assigned.AssignmentMethod;
                    }
                }

                if (assignmentsToCreate.Count > 0)
                {
                    await assignmentRepo.BulkInsertAsync(assignmentsToCreate);
                    _logger.LogInformation("Bulk inserted {Count} checkpoint assignments", assignmentsToCreate.Count);

                    // Update raw readings with assignment methods
                    var readingsToUpdate = dedupedReadings
                        .Where(r => !string.IsNullOrEmpty(r.AssignmentMethod))
                        .ToList();

                    if (readingsToUpdate.Count > 0)
                    {
                        await readingRepo.BulkUpdateAsync(readingsToUpdate);
                    }
                }

                // ================================================================
                // RESPONSE + SUMMARY
                // ================================================================
                response.CheckpointsAssigned = assignmentsToCreate.Count;
                response.ReadingsProcessed = readingInputs.Count;
                response.Status = "Completed";
                response.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

                var methodSummary = deduplicatedReadings
                    .GroupBy(r => r.AssignmentMethod)
                    .Select(g => $"{g.Key}={g.Count()}")
                    .ToList();

                var totalDeleted = staleAssignments.Count + remainingAssignments.Count;

                _logger.LogInformation(
                    "═══ Phase 1.5 COMPLETE ═══\n" +
                    "  Raw readings:     {Raw}\n" +
                    "  After cross-dedup: {Deduped} (removed {DupesRemoved} cross-batch dupes)\n" +
                    "  After pass-dedup:  {Collapsed} (collapsed {PassRemoved} same-pass reads)\n" +
                    "  Deleted prior:     {Deleted} ({Stale} stale + {Remaining} re-process)\n" +
                    "  Final assignments: {Final} in {Time}ms [{Methods}]",
                    allReadings.Count, dedupedReadings.Count, duplicatesRemoved,
                    totalCollapsed, readingInputs.Count - totalCollapsed,
                    totalDeleted, staleAssignments.Count, remainingAssignments.Count,
                    assignmentsToCreate.Count, response.ProcessingTimeMs,
                    string.Join(", ", methodSummary));

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error assigning checkpoints: {ex.Message}";
                _logger.LogError(ex, "Error in AssignCheckpointsForLoopRaceAsync");
                response.Status = "Failed";
                response.ErrorMessage = ErrorMessage;
                return response;
            }
        }

        /// <summary>
        /// Create split times from normalized readings.
        /// Calculates cumulative time from race start and segment time from previous checkpoint.
        /// </summary>
        public async Task<CreateSplitTimesResponse> CreateSplitTimesFromNormalizedReadingsAsync(string eventId, string raceId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var startTime = DateTime.UtcNow;

            var response = new CreateSplitTimesResponse
            {
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Creating split times for Race {RaceId}", decryptedRaceId);

                // Get race start time
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId)
                    .FirstOrDefaultAsync();

                if (race == null)
                {
                    ErrorMessage = "Race not found";
                    response.Status = "Failed";
                    response.ErrorMessage = ErrorMessage;
                    return response;
                }

                var raceStartTime = race.StartTime;

                // Get checkpoints ordered by distance to determine start checkpoint
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var checkpoints = await checkpointRepo.GetQuery(cp =>
                    cp.RaceId == decryptedRaceId &&
                    cp.EventId == decryptedEventId &&
                    cp.AuditProperties.IsActive &&
                    !cp.AuditProperties.IsDeleted)
                    .OrderBy(cp => cp.DistanceFromStart)
                    .ToListAsync();

                if (checkpoints.Count == 0)
                {
                    ErrorMessage = "No checkpoints found for race";
                    response.Status = "Failed";
                    response.ErrorMessage = ErrorMessage;
                    return response;
                }

                // Get the first checkpoint (start line) to use as FromCheckpointId for first split
                var startCheckpoint = checkpoints.First();

                _logger.LogInformation("Found {Count} checkpoints for race. Start checkpoint: {Name} (ID: {Id})",
                    checkpoints.Count, startCheckpoint.Name, startCheckpoint.Id);

                // Get all normalized readings ordered by participant and time
                // FIX: Filter by Participant.RaceId to ensure we only get readings for THIS race
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var normalizedReadings = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.Participant.RaceId == decryptedRaceId &&  // FIX: Only readings for THIS race's participants
                    rn.AuditProperties.IsActive &&
                    !rn.AuditProperties.IsDeleted)
                    .Include(rn => rn.Checkpoint)
                    .Include(rn => rn.Participant)  // Need to include for RaceId filter
                    .OrderBy(rn => rn.ParticipantId)
                    .ThenBy(rn => rn.ChipTime)
                    .ToListAsync();

                if (normalizedReadings.Count == 0)
                {
                    _logger.LogInformation("No normalized readings found to create split times");
                    response.Status = "Completed";
                    response.SplitTimesCreated = 0;
                    response.ParticipantsProcessed = 0;
                    response.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                    return response;
                }

                _logger.LogInformation("Found {Count} normalized readings to process", normalizedReadings.Count);

                // Get existing split times to avoid duplicates
                var splitTimeRepo = _repository.GetRepository<SplitTimes>();
                var existingSplitTimesList = await splitTimeRepo.GetQuery(st =>
                    st.EventId == decryptedEventId &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .Select(st => new { st.ParticipantId, st.CheckpointId })
                    .ToListAsync();

                var existingSplitTimes = existingSplitTimesList
                    .Select(st => $"{st.ParticipantId}_{st.CheckpointId}")
                    .ToHashSet();

                _logger.LogInformation("Found {Count} existing split times to skip", existingSplitTimes.Count);

                // Group by participant
                var readingsByParticipant = normalizedReadings
                    .GroupBy(r => r.ParticipantId)
                    .ToList();

                var splitTimesToCreate = new List<SplitTimes>();

                foreach (var participantGroup in readingsByParticipant)
                {
                    var participantId = participantGroup.Key;
                    var participantReadings = participantGroup.OrderBy(r => r.ChipTime).ToList();

                    // Track previous checkpoint for segment calculations
                    int? previousCheckpointId = null;
                    DateTime? previousCheckpointTime = raceStartTime;

                    foreach (var reading in participantReadings)
                    {
                        // Skip if split time already exists
                        var key = $"{participantId}_{reading.CheckpointId}";
                        if (existingSplitTimes.Contains(key))
                        {
                            _logger.LogDebug("Skipping existing split time for participant {ParticipantId}, checkpoint {CheckpointId}",
                                participantId, reading.CheckpointId);

                            // Update tracking variables even for existing splits
                            previousCheckpointId = reading.CheckpointId;
                            previousCheckpointTime = reading.ChipTime;
                            continue;
                        }

                        // Calculate split time (cumulative from race start)
                        long splitTimeMs = 0;
                        if (raceStartTime.HasValue)
                        {
                            splitTimeMs = (long)(reading.ChipTime - raceStartTime.Value).TotalMilliseconds;
                        }

                        // Calculate segment time (time since previous checkpoint)
                        long segmentTimeMs = 0;
                        if (previousCheckpointTime.HasValue)
                        {
                            segmentTimeMs = (long)(reading.ChipTime - previousCheckpointTime.Value).TotalMilliseconds;
                        }

                        // Validate: Skip readings that occurred before race start (invalid data)
                        if (splitTimeMs < 0)
                        {
                            _logger.LogWarning(
                                "Skipping split time for participant {ParticipantId} at checkpoint {CheckpointId}: " +
                                "Reading time {ReadTime} is before race start time {RaceStart} (splitTimeMs: {SplitTimeMs}ms)",
                                participantId, reading.CheckpointId, reading.ChipTime, raceStartTime, splitTimeMs);

                            // Still update previous checkpoint tracking to maintain sequence
                            previousCheckpointId = reading.CheckpointId;
                            previousCheckpointTime = reading.ChipTime;
                            continue;
                        }

                        // Validate: Skip if segment time is negative (out of order readings)
                        if (segmentTimeMs < 0)
                        {
                            _logger.LogWarning(
                                "Skipping split time for participant {ParticipantId} at checkpoint {CheckpointId}: " +
                                "Negative segment time detected (segmentTimeMs: {SegmentTimeMs}ms). Reading may be out of order.",
                                participantId, reading.CheckpointId, segmentTimeMs);

                            // Still update previous checkpoint tracking to maintain sequence
                            previousCheckpointId = reading.CheckpointId;
                            previousCheckpointTime = reading.ChipTime;
                            continue;
                        }

                        // Determine FromCheckpointId
                        int fromCheckpointId;
                        if (previousCheckpointId.HasValue)
                        {
                            // Use previous checkpoint
                            fromCheckpointId = previousCheckpointId.Value;
                        }
                        else
                        {
                            // First reading for this participant - use start checkpoint
                            fromCheckpointId = startCheckpoint.Id;
                        }

                        // Convert milliseconds to TimeSpan for the SplitTime column (legacy TIME column)
                        var splitTimeSpan = TimeSpan.FromMilliseconds(splitTimeMs);

                        // Ensure TimeSpan is within valid SQL TIME range (00:00:00 to 23:59:59.9999999)
                        // TIME column cannot handle negative values or values >= 24 hours
                        if (splitTimeSpan < TimeSpan.Zero)
                        {
                            _logger.LogWarning(
                                "Split time for participant {ParticipantId} is negative ({Time}). Setting to 00:00:00.",
                                participantId, splitTimeSpan);
                            splitTimeSpan = TimeSpan.Zero;
                        }
                        else if (splitTimeSpan.TotalHours >= 24)
                        {
                            _logger.LogWarning(
                                "Split time for participant {ParticipantId} exceeds 24 hours ({Hours}h). Capping at 23:59:59.",
                                participantId, splitTimeSpan.TotalHours);
                            splitTimeSpan = new TimeSpan(23, 59, 59);
                        }

                        var splitTime = new SplitTimes
                        {
                            ParticipantId = participantId,
                            EventId = decryptedEventId,
                            CheckpointId = reading.CheckpointId,
                            ReadNormalizedId = reading.Id,

                            // REQUIRED: Define the segment
                            FromCheckpointId = fromCheckpointId,
                            ToCheckpointId = reading.CheckpointId,

                            // REQUIRED: Legacy TIME column
                            SplitTime = splitTimeSpan,

                            // Modern time columns (milliseconds)
                            SplitTimeMs = splitTimeMs,      // Cumulative from race start
                            SegmentTime = segmentTimeMs,    // Time for this segment only

                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        splitTimesToCreate.Add(splitTime);

                        // Update tracking variables for next iteration
                        previousCheckpointId = reading.CheckpointId;
                        previousCheckpointTime = reading.ChipTime;
                    }
                }

                // Bulk insert split times
                if (splitTimesToCreate.Count > 0)
                {
                    _logger.LogInformation("Bulk inserting {Count} split times", splitTimesToCreate.Count);
                    await splitTimeRepo.BulkInsertAsync(splitTimesToCreate);
                    _logger.LogInformation("Successfully created {Count} split times", splitTimesToCreate.Count);
                }
                else
                {
                    _logger.LogInformation("No new split times to create (all already exist)");
                }

                response.SplitTimesCreated = splitTimesToCreate.Count;
                response.ParticipantsProcessed = readingsByParticipant.Count;
                response.Status = "Completed";
                response.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation(
                    "Split time creation completed. Created: {Created}, Participants: {Participants}, Time: {Time}ms",
                    response.SplitTimesCreated, response.ParticipantsProcessed, response.ProcessingTimeMs);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error creating split times: {ex.Message}";
                _logger.LogError(ex, "Error creating split times for race {RaceId}", decryptedRaceId);
                response.Status = "Failed";
                response.ErrorMessage = ErrorMessage;
                return response;
            }
        }
    }
}