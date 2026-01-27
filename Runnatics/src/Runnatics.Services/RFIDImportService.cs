using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.Participants;
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

                // Validate Excel file
                var isExcel = request.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                             request.File.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);

                if (!isExcel)
                {
                    ErrorMessage = "Only Excel files (.xlsx, .xls) are supported";
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
                catch (Exception ex)
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

                // Get device ID
                var deviceId = request.DeviceId ?? "Unknown";
                int? checkpointId = null;
                if (!string.IsNullOrEmpty(request.CheckpointId))
                {
                    checkpointId = Convert.ToInt32(_encryptionService.Decrypt(request.CheckpointId));
                }

                // Create batch record
                var batch = new UploadBatch
                {
                    RaceId = decryptedRaceId,
                    EventId = decryptedEventId,
                    DeviceId = deviceId,
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

                // Parse SQLite file
                var readings = await ParseSqliteFileAsync(
                    tempFilePath,
                    batch.Id,
                    deviceId,
                    request.TimeZoneId,
                    request.TreatAsUtc
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

                response.ImportBatchId = _encryptionService.Encrypt(batch.Id.ToString());
                response.TotalRecords = readings.Count;
                response.ValidRecords = readings.Count;
                response.InvalidRecords = 0;
                response.Status = "Uploaded";

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

        public async Task<ProcessRFIDImportResponse> ProcessRFIDStagingDataAsync(ProcessRFIDImportRequest request)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));
            var decryptedImportBatchId = Convert.ToInt32(_encryptionService.Decrypt(request.ImportBatchId));

            var response = new ProcessRFIDImportResponse
            {
                ImportBatchId = decryptedImportBatchId,
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting RFID processing for ImportBatch {BatchId}", decryptedImportBatchId);

                // Get import batch
                var batchRepo = _repository.GetRepository<UploadBatch>();
                var importBatch = await batchRepo.GetQuery(b =>
                    b.Id == decryptedImportBatchId &&
                    b.RaceId == decryptedRaceId &&
                    b.EventId == decryptedEventId)
                    .FirstOrDefaultAsync();

                if (importBatch == null)
                {
                    ErrorMessage = "Import batch not found";
                    _logger.LogWarning("Import batch {BatchId} not found", decryptedImportBatchId);
                    response.Status = "Failed";
                    return response;
                }

                // Get pending readings
                var readingRepo = _repository.GetRepository<RawRFIDReading>();
                var readings = await readingRepo.GetQuery(r =>
                    r.BatchId == decryptedImportBatchId &&
                    r.ProcessResult == "Pending")
                    .ToListAsync();

                if (readings.Count == 0)
                {
                    ErrorMessage = "No pending readings to process";
                    _logger.LogWarning("No pending readings for ImportBatch {BatchId}", decryptedImportBatchId);
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

                await _repository.BeginTransactionAsync();

                try
                {
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

                                // If checkpoint is specified in batch, create assignment
                                if (importBatch.ExpectedCheckpointId.HasValue)
                                {
                                    var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                                    var assignment = new ReadingCheckpointAssignment
                                    {
                                        ReadingId = reading.Id,
                                        CheckpointId = importBatch.ExpectedCheckpointId.Value,
                                        AuditProperties = new Models.Data.Common.AuditProperties
                                        {
                                            CreatedBy = userId,
                                            CreatedDate = DateTime.UtcNow,
                                            IsActive = true,
                                            IsDeleted = false
                                        }
                                    };
                                    await assignmentRepo.AddAsync(assignment);
                                    reading.AssignmentMethod = "Batch";
                                }
                            }

                            reading.ProcessedAt = DateTime.UtcNow;
                            await readingRepo.UpdateAsync(reading);
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
                            await readingRepo.UpdateAsync(reading);
                            errorCount++;
                        }
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
                catch (Exception ex)
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
                _logger.LogError(ex, "Error processing RFID import batch {BatchId}", decryptedImportBatchId);
                response.Status = "Failed";
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

                // Get readings that are successfully processed but not yet normalized
                var rawReadings = await (
                    from r in readingRepo.GetQuery(r =>
                        r.ProcessResult == "Success" &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                    join a in assignmentRepo.GetQuery()
                        on r.Id equals a.ReadingId
                    join ca in chipAssignmentRepo.GetQuery(ca =>
                        ca.Participant.RaceId == decryptedRaceId &&
                        !ca.UnassignedAt.HasValue)
                        .Include(ca => ca.Participant)
                        .Include(ca => ca.Chip)
                        on r.Epc equals ca.Chip.EPC
                    select new
                    {
                        Reading = r,
                        CheckpointId = a.CheckpointId,
                        ParticipantId = ca.ParticipantId,
                        RawReadId = r.Id
                    }
                ).ToListAsync();
                
                // Filter out already normalized readings
                var existingNormalizedReadIds = await normalizedRepo.GetQuery()
                    .Select(n => n.RawReadId)
                    .ToListAsync();
                    
                rawReadings = rawReadings
                    .Where(r => !existingNormalizedReadIds.Contains(r.RawReadId))
                    .ToList();

                response.TotalRawReadings = rawReadings.Count;

                // Group by Participant + Checkpoint
                var grouped = rawReadings
                    .GroupBy(r => new { r.ParticipantId, r.CheckpointId })
                    .ToList();

                response.CheckpointsProcessed = grouped.Select(g => g.Key.CheckpointId).Distinct().Count();
                response.ParticipantsProcessed = grouped.Select(g => g.Key.ParticipantId).Distinct().Count();

                var normalizedReadings = new List<ReadNormalized>();
                var duplicateCount = 0;

                await _repository.BeginTransactionAsync();

                try
                {
                    foreach (var group in grouped)
                    {
                        // Sort by timestamp (earliest first), then by RSSI (strongest first)
                        var orderedReadings = group
                            .OrderBy(r => r.Reading.TimestampMs)
                            .ThenByDescending(r => r.Reading.RssiDbm ?? decimal.MinValue)
                            .ToList();

                        // Keep only the first reading (earliest with strongest RSSI)
                        var bestReading = orderedReadings.First();
                        duplicateCount += orderedReadings.Count - 1;

                        // Calculate GunTime (milliseconds from race start)
                        long? gunTime = null;
                        if (raceStartTime.HasValue)
                        {
                            var readTime = bestReading.Reading.ReadTimeUtc;
                            gunTime = (long)(readTime - raceStartTime.Value).TotalMilliseconds;
                        }

                        // Create normalized reading
                        var normalized = new ReadNormalized
                        {
                            EventId = decryptedEventId,
                            ParticipantId = bestReading.ParticipantId,
                            CheckpointId = bestReading.CheckpointId,
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

                        normalizedReadings.Add(normalized);
                    }

                    // Bulk insert normalized readings
                    await normalizedRepo.AddRangeAsync(normalizedReadings);
                    await _repository.SaveChangesAsync();
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
                catch (Exception ex)
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
    }
}
