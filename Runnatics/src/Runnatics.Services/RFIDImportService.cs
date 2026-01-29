using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace Runnatics.Services
{
    public class RFIDImportService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IRFIDImportService
    {
        private readonly IMapper _mapper;
        private readonly ILogger<RFIDImportService> _logger;
        private readonly IUserContextService _userContext;
        private readonly IEncryptionService _encryptionService;

        public RFIDImportService(
            IUnitOfWork<RaceSyncDbContext> repository,
            IMapper mapper,
            ILogger<RFIDImportService> logger,
            IUserContextService userContext,
            IEncryptionService encryptionService)
            : base(repository)
        {
            _mapper = mapper;
            _logger = logger;
            _userContext = userContext;
            _encryptionService = encryptionService;
        }

        public async Task<EPCMappingImportResponse> UploadEPCMappingAsync(string eventId, string raceId, EPCMappingImportRequest request)
        {
            var userId = _userContext.UserId;
            var tenantId = _userContext.TenantId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

            var response = new EPCMappingImportResponse
            {
                FileName = request.File.FileName,
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting EPC-BIB mapping upload for Event {EventId}, Race {RaceId}", decryptedEventId, decryptedRaceId);

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

                // Get participants by BIB number
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .ToListAsync();

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
                    b.RaceId == decryptedRaceId)
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

                // Encrypt IDs and delegate to existing upload method
                var encryptedEventId = _encryptionService.Encrypt(checkpoint.EventId.ToString());
                var encryptedRaceId = _encryptionService.Encrypt(checkpoint.RaceId.ToString());

                // Update request with discovered checkpoint info
                request.ExpectedCheckpointId = _encryptionService.Encrypt(checkpoint.Id.ToString());
                request.DeviceId = device.Name;

                _logger.LogInformation("Auto-detection complete. Delegating to UploadRFIDFileAsync with EventId: {EventId}, RaceId: {RaceId}",
                    checkpoint.EventId, checkpoint.RaceId);

                // Delegate to existing upload method with discovered IDs
                return await UploadRFIDFileAsync(encryptedEventId, encryptedRaceId, request);
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

                        existingAssignmentIds = new HashSet<long>(existing);
                    }

                    // **GROUP READINGS BY PARTICIPANT** for loop race processing
                    Dictionary<int, List<RawRFIDReading>> readingsByParticipant = new Dictionary<int, List<RawRFIDReading>>();
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
                                    readingsByParticipant[participantId] = new List<RawRFIDReading>();
                                }
                                readingsByParticipant[participantId].Add(reading);
                            }
                        }

                        // Sort each participant's readings by timestamp
                        foreach (var participantReadings in readingsByParticipant.Values)
                        {
                            participantReadings.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));
                        }

                        _logger.LogInformation(
                            "Loop race mode: Grouped {TotalReadings} readings into {ParticipantCount} participants",
                            readings.Count, readingsByParticipant.Count);
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
                                    // **LOOP RACE MODE**: Assign based on reading sequence
                                    var participantReadings = readingsByParticipant[participant.Id];
                                    int readingIndex = participantReadings.IndexOf(reading);

                                    if (readingIndex >= 0 && readingIndex < deviceCheckpoints.Count)
                                    {
                                        assignedCheckpointId = deviceCheckpoints[readingIndex].Id;
                                        _logger.LogDebug(
                                            "Loop race: Participant {ParticipantId} reading #{Index} at {Time} assigned to checkpoint '{CheckpointName}' ({Distance}KM)",
                                            participant.Id, readingIndex + 1, reading.ReadTimeUtc.ToString("HH:mm:ss"),
                                            deviceCheckpoints[readingIndex].Name, deviceCheckpoints[readingIndex].DistanceFromStart);
                                    }
                                    else
                                    {
                                        _logger.LogWarning(
                                            "Loop race: Participant {ParticipantId} has extra reading #{Index} beyond {MaxCheckpoints} checkpoints - skipping assignment",
                                            participant.Id, readingIndex + 1, deviceCheckpoints.Count);
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
        public async Task<BulkProcessRFIDImportResponse> ProcessAllRFIDDataAsync(string eventId, string raceId)
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
                _logger.LogInformation("Starting bulk RFID processing for Race {RaceId}", decryptedRaceId);

                // Get ALL pending batches for this event/race
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var pendingBatches = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    b.RaceId == decryptedRaceId &&
                    (b.Status == "uploaded" || b.Status == "uploading") &&
                    b.AuditProperties.IsActive &&
                    !b.AuditProperties.IsDeleted)
                    .AsNoTracking() // Read-only query, no need to track
                    .OrderBy(b => b.AuditProperties.CreatedDate)
                    .ToListAsync();

                if (pendingBatches.Count == 0)
                {
                    response.Status = "NoDataToProcess";
                    response.Message = "No pending batches found for this race";
                    _logger.LogInformation("No pending batches found for Race {RaceId}", decryptedRaceId);
                    return response;
                }

                response.TotalBatches = pendingBatches.Count;
                _logger.LogInformation("Processing {Count} batches for Race {RaceId}", pendingBatches.Count, decryptedRaceId);

                // Process each batch
                foreach (var batch in pendingBatches)
                {
                    var batchResult = new BatchProcessResult
                    {
                        BatchId = _encryptionService.Encrypt(batch.Id.ToString()),
                        FileName = batch.OriginalFileName,
                        DeviceId = batch.DeviceId,
                        Status = "Processing"
                    };

                    try
                    {
                        _logger.LogInformation("Processing batch {BatchId} - {FileName}", batch.Id, batch.OriginalFileName);

                        // Reuse existing single-batch processing logic
                        var processRequest = new ProcessRFIDImportRequest
                        {
                            EventId = eventId,
                            RaceId = raceId,
                            UploadBatchId = _encryptionService.Encrypt(batch.Id.ToString())
                        };

                        var result = await ProcessRFIDStagingDataAsync(processRequest);

                        batchResult.Status = result.Status;
                        batchResult.SuccessCount = result.SuccessCount;
                        batchResult.ErrorCount = result.ErrorCount;
                        batchResult.UnlinkedCount = result.UnlinkedCount;

                        if (result.Status == "Completed" || result.Status == "CompletedWithErrors")
                        {
                            response.SuccessfulBatches++;
                            response.TotalProcessedReadings += result.SuccessCount;
                        }
                        else
                        {
                            response.FailedBatches++;
                            batchResult.ErrorMessage = "Processing failed";
                        }

                        _logger.LogInformation(
                            "Batch {BatchId} processed. Success: {Success}, Errors: {Errors}",
                            batch.Id, result.SuccessCount, result.ErrorCount);
                    }
                    catch (Exception ex)
                    {
                        batchResult.Status = "Failed";
                        batchResult.ErrorMessage = ex.Message;
                        response.FailedBatches++;
                        _logger.LogError(ex, "Failed to process batch {BatchId}", batch.Id);
                    }

                    response.BatchResults.Add(batchResult);
                }

                response.Status = response.FailedBatches > 0 ? "CompletedWithErrors" : "Completed";
                response.Message = $"Processed {response.SuccessfulBatches} of {response.TotalBatches} batches successfully";

                _logger.LogInformation(
                    "Bulk processing completed. Success: {Success}, Failed: {Failed}, Total Readings: {Readings}",
                    response.SuccessfulBatches, response.FailedBatches, response.TotalProcessedReadings);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during bulk processing: {ex.Message}";
                _logger.LogError(ex, "Error processing all RFID data for race {RaceId}", decryptedRaceId);
                response.Status = "Failed";
                response.Message = ex.Message;
                return response;
            }
        }

        private async Task<List<RawRFIDReading>> ParseSqliteFileAsync(
            string filePath,
            int batchId,
            string deviceId,
            string timeZoneId,
            bool treatAsUtc)
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

                // Get raw readings that haven't been normalized yet
                var rawReadingsQuery = await readingRepo.GetQuery(r =>
                        r.ProcessResult == "Success" &&
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

                // Process all groups in parallel using LINQ - no for loop needed
                var normalizedReadings = grouped.Select(group =>
                {
                    // Get the best reading (earliest timestamp, strongest RSSI)
                    // This now includes readings from both parent and child devices merged together
                    var bestReading = group
                        .OrderBy(r => r.Reading.TimestampMs)
                        .ThenByDescending(r => r.Reading.RssiDbm ?? decimal.MinValue)
                        .First();

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
                            "Using earliest reading at {Time}",
                            group.Key.ParticipantId, group.Key.CheckpointId, group.Count(), mergeInfo, timeSpreadSeconds,
                            bestReading.Reading.ReadTimeUtc.ToString("HH:mm:ss"));
                    }

                    // Calculate GunTime (milliseconds from race start)
                    long? gunTime = null;
                    if (raceStartTime.HasValue)
                    {
                        gunTime = (long)(bestReading.Reading.ReadTimeUtc - raceStartTime.Value).TotalMilliseconds;
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
                        NetTime = null, // TODO: Calculate net time (from participant start)
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
                        "Deduplication completed. Normalized: {Normalized}, Duplicates: {Duplicates}, Time: {Time}ms",
                        normalizedReadings.Count, duplicateCount, response.ProcessingTimeMs);

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

        /// <summary>
        /// Calculate race results from normalized readings and insert into Results table.
        /// Calculates overall, gender, and category rankings.
        /// </summary>
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

                // Get finish checkpoint (checkpoint with maximum distance from start)
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var finishCheckpoint = await checkpointRepo.GetQuery(cp =>
                    cp.RaceId == decryptedRaceId &&
                    cp.EventId == decryptedEventId &&
                    cp.AuditProperties.IsActive &&
                    !cp.AuditProperties.IsDeleted)
                    .OrderByDescending(cp => cp.DistanceFromStart)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (finishCheckpoint == null)
                {
                    ErrorMessage = "No finish checkpoint found for this race";
                    response.Status = "Failed";
                    return response;
                }

                _logger.LogInformation("Using checkpoint {CheckpointId} as finish line (Distance: {Distance})",
                    finishCheckpoint.Id, finishCheckpoint.DistanceFromStart);

                // Get normalized readings at finish checkpoint with participant data
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var finishReadings = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.CheckpointId == finishCheckpoint.Id &&
                    rn.AuditProperties.IsActive &&
                    !rn.AuditProperties.IsDeleted)
                    .Include(rn => rn.Participant)
                    .OrderBy(rn => rn.GunTime ?? long.MaxValue)
                    .ToListAsync();

                if (finishReadings.Count == 0)
                {
                    response.Status = "Completed";
                    response.Message = "No finish line readings found. Run deduplication first.";
                    return response;
                }

                // Get existing results to check for updates vs inserts
                var resultsRepo = _repository.GetRepository<Results>();
                var existingResults = await resultsRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId)
                    .ToDictionaryAsync(r => r.ParticipantId, r => r);

                // Get all registered participants for DNF calculation
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var totalParticipants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .CountAsync();

                await _repository.BeginTransactionAsync();

                try
                {
                    var resultsToAdd = new List<Results>();
                    var resultsToUpdate = new List<Results>();

                    // Process all readings using LINQ with index - no for loop needed
                    var processedResults = finishReadings.Select((reading, index) =>
                    {
                        var overallRank = index + 1;

                        if (existingResults.TryGetValue(reading.ParticipantId, out var existingResult))
                        {
                            // Update existing result
                            existingResult.FinishTime = reading.GunTime;
                            existingResult.GunTime = reading.GunTime;
                            existingResult.NetTime = reading.NetTime;
                            existingResult.OverallRank = overallRank;
                            existingResult.Status = "Finished";
                            existingResult.AuditProperties.UpdatedBy = userId;
                            existingResult.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            return (result: existingResult, isNew: false);
                        }
                        else
                        {
                            // Create new result
                            var result = new Results
                            {
                                EventId = decryptedEventId,
                                RaceId = decryptedRaceId,
                                ParticipantId = reading.ParticipantId,
                                FinishTime = reading.GunTime,
                                GunTime = reading.GunTime,
                                NetTime = reading.NetTime,
                                OverallRank = overallRank,
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
                            };
                            return (result: result, isNew: true);
                        }
                    }).ToList();

                    resultsToAdd = processedResults.Where(r => r.isNew).Select(r => r.result).ToList();
                    resultsToUpdate = processedResults.Where(r => !r.isNew).Select(r => r.result).ToList();

                    // TRUE BULK INSERT - Single DB roundtrip
                    if (resultsToAdd.Count > 0)
                    {
                        await resultsRepo.BulkInsertAsync(resultsToAdd);
                        _logger.LogInformation("Bulk inserted {Count} new results", resultsToAdd.Count);
                    }

                    // TRUE BULK UPDATE - Single DB roundtrip
                    if (resultsToUpdate.Count > 0)
                    {
                        await resultsRepo.BulkUpdateAsync(resultsToUpdate);
                        _logger.LogInformation("Bulk updated {Count} existing results", resultsToUpdate.Count);
                    }

                    await _repository.SaveChangesAsync();

                    // Calculate gender rankings
                    await CalculateGenderRankingsAsync(decryptedEventId, decryptedRaceId, userId);

                    // Calculate category rankings
                    var categoriesProcessed = await CalculateCategoryRankingsAsync(decryptedEventId, decryptedRaceId, userId);

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    // Calculate gender stats for response
                    var genderStats = finishReadings
                        .GroupBy(r => r.Participant?.Gender?.ToLower() ?? "other")
                        .ToDictionary(g => g.Key, g => g.Count());

                    response.TotalFinishers = finishReadings.Count;
                    response.ResultsCreated = resultsToAdd.Count;
                    response.ResultsUpdated = resultsToUpdate.Count;
                    response.DNFCount = totalParticipants - finishReadings.Count;
                    response.CategoriesProcessed = categoriesProcessed;
                    response.GenderStats = new GenderBreakdown
                    {
                        MaleFinishers = genderStats.GetValueOrDefault("male", 0),
                        FemaleFinishers = genderStats.GetValueOrDefault("female", 0),
                        OtherFinishers = genderStats.GetValueOrDefault("other", 0)
                    };
                    response.Status = "Completed";
                    response.Message = $"Successfully calculated results for {finishReadings.Count} finishers";

                    var endTime = DateTime.UtcNow;
                    response.ProcessingTimeMs = (long)(endTime - startTime).TotalMilliseconds;

                    _logger.LogInformation(
                        "Race results calculated. Finishers: {Finishers}, Created: {Created}, Updated: {Updated}, Time: {Time}ms",
                        finishReadings.Count, resultsToAdd.Count, resultsToUpdate.Count, response.ProcessingTimeMs);

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

        /// <summary>
        /// Calculate gender-based rankings for all results in a race - Optimized with LINQ (no for loops)
        /// </summary>
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

        /// <summary>
        /// Calculate category-based rankings for all results in a race - Optimized with LINQ (no for loops)
        /// </summary>
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

        /// <summary>
        /// Complete RFID processing workflow: Process all pending batches, deduplicate readings, and calculate results.
        /// Optimized with bulk operations for best performance.
        /// </summary>
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

                // ========== PHASE 1: Process All Pending Batches ==========
                var phase1Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 1: Processing pending batches...");

                var processAllResponse = await ProcessAllRFIDDataAsync(eventId, raceId);

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
                    "Phase 1 completed in {Time}ms. Batches: {Success}/{Total}, Readings: {Readings}",
                    response.Phase1ProcessingMs,
                    processAllResponse.SuccessfulBatches,
                    processAllResponse.TotalBatches,
                    processAllResponse.TotalProcessedReadings);

                // ========== PHASE 1.5: Assign Checkpoints (Loop Races) ==========
                var phase15Start = DateTime.UtcNow;
                _logger.LogInformation("Phase 1.5: Assigning checkpoints for loop races...");

                var assignResponse = await AssignCheckpointsForLoopRaceAsync(eventId, raceId);

                response.Phase15AssignmentMs = (long)(DateTime.UtcNow - phase15Start).TotalMilliseconds;
                response.CheckpointsAssigned = assignResponse.CheckpointsAssigned;

                if (assignResponse.Status == "Failed")
                {
                    response.Warnings.Add("Phase 1.5 failed: Checkpoint assignment error (may affect loop races)");
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
                    "Phase 3 completed in {Time}ms. Finishers: {Finishers}, Results created: {Created}, Updated: {Updated}",
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
                    "Complete RFID workflow finished in {TotalTime}ms. Status: {Status}. " +
                    "Batches: {Batches}, Readings: {Readings}, Normalized: {Normalized}, Finishers: {Finishers}",
                    response.TotalProcessingTimeMs,
                    response.Status,
                    response.TotalBatchesProcessed,
                    response.TotalRawReadingsProcessed,
                    response.TotalNormalizedReadings,
                    response.TotalFinishers);

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
                    await resultsRepo.DeleteRangeAsync(results.Select(r => r.Id).ToList());
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
                    await normalizedRepo.DeleteRangeAsync(normalized.Select(n => n.Id).ToList());
                    response.NormalizedReadingsCleared = normalized.Count;
                    _logger.LogInformation("Cleared {Count} normalized readings", normalized.Count);
                }

                // 3. Get batch IDs for this race
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var batches = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    b.RaceId == decryptedRaceId)
                    .ToListAsync();

                var batchIds = batches.Select(b => b.Id).ToList();

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
                    await assignmentRepo.DeleteRangeAsync(assignments.Select(a => a.Id).ToList());
                    response.AssignmentsCleared = assignments.Count;
                    _logger.LogInformation("Cleared {Count} checkpoint assignments", assignments.Count);
                }

                // 5. Reset RawRFIDReading status
                var readings = await readingRepo.GetQuery(r => batchIds.Contains(r.BatchId))
                    .ToListAsync();

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

                // 6. Reset UploadBatch status
                if (batches.Count > 0)
                {
                    foreach (var batch in batches)
                    {
                        batch.Status = "uploaded";
                        batch.ProcessingStartedAt = null;
                        batch.ProcessingCompletedAt = null;
                    }
                    await batchRepo.UpdateRangeAsync(batches);
                    response.BatchesReset = batches.Count;
                    _logger.LogInformation("Reset {Count} batches to uploaded status", batches.Count);
                }

                // 7. Optionally delete uploads completely
                if (!keepUploads)
                {
                    if (readings.Count > 0)
                    {
                        // Use long overload for RawRFIDReading.Id
                        var readingIdsToDelete = readings.Select(r => r.Id).ToList();
                        await readingRepo.DeleteRangeAsync(readingIdsToDelete);
                    }
                    if (batches.Count > 0)
                    {
                        await batchRepo.DeleteRangeAsync(batches.Select(b => b.Id).ToList());
                        response.UploadsDeleted = batches.Count;
                        _logger.LogInformation("Deleted {Count} upload batches", batches.Count);
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
        public async Task<ReprocessParticipantsResponse> ReprocessParticipantsAsync(
            string eventId,
            string raceId,
            string[] participantIds)
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
        public async Task<ProcessRFIDImportResponse> ReprocessBatchAsync(
            string eventId,
            string raceId,
            string uploadBatchId)
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
        /// Readings are assigned to checkpoints based on their time sequence per participant.
        /// </summary>
        public async Task<AssignCheckpointsResponse> AssignCheckpointsForLoopRaceAsync(string eventId, string raceId)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var startTime = DateTime.UtcNow;

            var response = new AssignCheckpointsResponse
            {
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Assigning checkpoints for loop race {RaceId}", decryptedRaceId);

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

                var raceStartTime = race.StartTime ?? DateTime.UtcNow;

                // Get checkpoints ordered by distance
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

                // Get active chip assignments to map EPC to participant
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                var chipAssignments = await chipAssignmentRepo.GetQuery(ca =>
                    ca.Participant.RaceId == decryptedRaceId &&
                    !ca.UnassignedAt.HasValue &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted)
                    .Include(ca => ca.Chip)
                    .Select(ca => new { ca.ParticipantId, EPC = ca.Chip.EPC })
                    .ToListAsync();

                var epcToParticipant = chipAssignments.ToDictionary(ca => ca.EPC, ca => ca.ParticipantId);

                // Get readings that need checkpoint assignment
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();

                // Get existing assignments
                var existingAssignmentReadingIds = await assignmentRepo.GetQuery(a =>
                    a.AuditProperties.IsActive &&
                    !a.AuditProperties.IsDeleted)
                    .Select(a => a.ReadingId)
                    .ToListAsync();

                var existingAssignmentSet = new HashSet<long>(existingAssignmentReadingIds);

                // Get batches for this race
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var batchIds = await batchRepo.GetQuery(b =>
                    b.EventId == decryptedEventId &&
                    b.RaceId == decryptedRaceId)
                    .Select(b => b.Id)
                    .ToListAsync();

                var unassignedReadings = await readingRepo.GetQuery(r =>
                    r.ProcessResult == "Success" &&
                    batchIds.Contains(r.BatchId) &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .ToListAsync();

                // Filter to only unassigned readings
                unassignedReadings = unassignedReadings
                    .Where(r => !existingAssignmentSet.Contains(r.Id))
                    .OrderBy(r => r.Epc)
                    .ThenBy(r => r.ReadTimeUtc)
                    .ToList();

                _logger.LogInformation("Found {Count} readings needing checkpoint assignment", unassignedReadings.Count);

                // Group by participant (EPC)
                var readingsByEpc = unassignedReadings
                    .GroupBy(r => r.Epc)
                    .ToList();

                var assignmentsToCreate = new List<ReadingCheckpointAssignment>();
                var flaggedForReview = 0;

                foreach (var epcGroup in readingsByEpc)
                {
                    var epc = epcGroup.Key;
                    var epcReadings = epcGroup.OrderBy(r => r.ReadTimeUtc).ToList();

                    // Assign readings to checkpoints sequentially
                    for (int i = 0; i < epcReadings.Count && i < checkpoints.Count; i++)
                    {
                        var reading = epcReadings[i];
                        var checkpoint = checkpoints[i];

                        var elapsedSeconds = (reading.ReadTimeUtc - raceStartTime).TotalSeconds;

                        // Simple validation: just check if reading is after race start
                        if (elapsedSeconds < 0)
                        {
                            _logger.LogWarning("EPC {Epc}: Reading before race start", epc);
                            flaggedForReview++;
                            continue;
                        }

                        var assignment = new ReadingCheckpointAssignment
                        {
                            ReadingId = reading.Id,
                            CheckpointId = checkpoint.Id,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        assignmentsToCreate.Add(assignment);
                    }

                    // Flag extra readings beyond checkpoint count
                    if (epcReadings.Count > checkpoints.Count)
                    {
                        _logger.LogWarning("EPC {Epc} has {Extra} extra readings beyond {Max} checkpoints",
                            epc, epcReadings.Count - checkpoints.Count, checkpoints.Count);
                        flaggedForReview += epcReadings.Count - checkpoints.Count;
                    }
                }

                // Bulk insert assignments
                if (assignmentsToCreate.Count > 0)
                {
                    await assignmentRepo.BulkInsertAsync(assignmentsToCreate);
                    _logger.LogInformation("Created {Count} checkpoint assignments", assignmentsToCreate.Count);
                }

                response.CheckpointsAssigned = assignmentsToCreate.Count;
                response.ReadingsProcessed = unassignedReadings.Count;
                response.FlaggedForReview = flaggedForReview;
                response.Status = "Completed";
                response.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error assigning checkpoints: {ex.Message}";
                _logger.LogError(ex, "Error assigning checkpoints");
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
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var normalizedReadings = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.AuditProperties.IsActive &&
                    !rn.AuditProperties.IsDeleted)
                    .Include(rn => rn.Checkpoint)
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

                        // Ensure TimeSpan doesn't exceed max TIME value (23:59:59.9999999)
                        if (splitTimeSpan.TotalHours >= 24)
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

