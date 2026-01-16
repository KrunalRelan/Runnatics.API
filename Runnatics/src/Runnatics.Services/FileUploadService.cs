using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.FileUpload;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Service for file upload operations
    /// </summary>
    public class FileUploadService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IFileUploadService
    {
        private readonly ILogger<FileUploadService> _logger;
        private readonly IFileParserFactory _parserFactory;
        private readonly IEncryptionService _encryptionService;
        private readonly string _uploadPath;

        public FileUploadService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<FileUploadService> logger,
            IFileParserFactory parserFactory,
            IEncryptionService encryptionService,
            IConfiguration configuration) : base(repository)
        {
            _logger = logger;
            _parserFactory = parserFactory;
            _encryptionService = encryptionService;
            _uploadPath = configuration["FileUpload:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
            }
        }

        public async Task<FileUploadResponse> UploadFileAsync(FileUploadFormRequest request, int userId)
        {
            var file = request.File;

            // Validate file
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("No file uploaded");
            }

            // Decrypt IDs
            var decryptedRaceId = TryParseOrDecrypt(request.RaceId, nameof(request.RaceId));
            var decryptedEventId = TryParseOrDecryptNullable(request.EventId, nameof(request.EventId));
            var decryptedCheckpointId = TryParseOrDecryptNullable(request.CheckpointId, nameof(request.CheckpointId));
            var decryptedReaderDeviceId = TryParseOrDecryptNullable(request.ReaderDeviceId, nameof(request.ReaderDeviceId));
            var decryptedMappingId = TryParseOrDecryptNullable(request.MappingId, nameof(request.MappingId));

            // ============================================
            // FIX: Added .db extension for SQLite files from R700
            // ============================================
            var allowedExtensions = new[] { ".csv", ".json", ".txt", ".db" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException($"Invalid file type. Allowed types: {string.Join(", ", allowedExtensions)}");
            }

            // Generate unique filename
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(_uploadPath, storedFileName);

            // Calculate file hash
            string fileHash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using var stream = file.OpenReadStream();
                var hash = await md5.ComputeHashAsync(stream);
                fileHash = Convert.ToHexString(hash);
            }

            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            // Check for duplicate file
            var existingBatch = await batchRepo.GetQuery(
                    b => b.FileHash == fileHash && b.RaceId == decryptedRaceId && !b.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (existingBatch != null)
            {
                _logger.LogWarning("Duplicate file detected: {FileName} matches batch {BatchId}",
                    file.FileName, existingBatch.Id);

                return new FileUploadResponse
                {
                    BatchId = existingBatch.Id,
                    BatchGuid = existingBatch.BatchGuid,
                    FileName = file.FileName,
                    FileSize = file.Length,
                    FileFormat = existingBatch.FileFormat,
                    Status = existingBatch.ProcessingStatus,
                    Message = $"Duplicate file detected. Matches existing batch {existingBatch.Id}."
                };
            }

            // Detect file format
            var detectedFormat = request.FileFormat ?? DetectFileFormat(file.FileName, extension);

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Create batch record
            var batch = new FileUploadBatch
            {
                RaceId = decryptedRaceId,
                EventId = decryptedEventId,
                ReaderDeviceId = decryptedReaderDeviceId,
                CheckpointId = decryptedCheckpointId,
                Description = request.Description,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FileSizeBytes = file.Length,
                FileFormat = detectedFormat,
                FileHash = fileHash,
                UploadedByUserId = userId,
                ProcessingStatus = FileProcessingStatus.Pending,
                AuditProperties = new AuditProperties
                {
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = userId,
                    IsActive = true,
                    IsDeleted = false
                }
            };

            await batchRepo.AddAsync(batch);
            await _repository.SaveChangesAsync();

            _logger.LogInformation("File uploaded successfully: {FileName} -> Batch {BatchId}, Format: {Format}",
                file.FileName, batch.Id, detectedFormat);

            return new FileUploadResponse
            {
                BatchId = batch.Id,
                BatchGuid = batch.BatchGuid,
                FileName = file.FileName,
                FileSize = file.Length,
                FileFormat = detectedFormat,
                Status = FileProcessingStatus.Pending,
                Message = "File uploaded successfully. Processing will begin shortly."
            };
        }

        public async Task<FileUploadStatusDto> GetBatchStatusAsync(int batchId)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            var batch = await batchRepo.GetQuery(
                    b => b.Id == batchId && !b.AuditProperties.IsDeleted,
                    includeNavigationProperties: true)
                .Include(b => b.UploadedByUser)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (batch == null)
            {
                throw new KeyNotFoundException($"Batch {batchId} not found");
            }

            return MapToStatusDto(batch);
        }

        public async Task<FileUploadStatusDto?> GetBatchStatusByGuidAsync(Guid batchGuid)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            var batch = await batchRepo.GetQuery(
                    b => b.BatchGuid == batchGuid && !b.AuditProperties.IsDeleted,
                    includeNavigationProperties: true)
                .Include(b => b.UploadedByUser)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            return batch == null ? null : MapToStatusDto(batch);
        }

        public async Task<FileUploadBatchListDto> GetBatchesAsync(string raceId, int pageNumber = 1, int pageSize = 20)
        {
            var decryptedRaceId = TryParseOrDecrypt(raceId, nameof(raceId));
            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            var query = batchRepo.GetQuery(
                    b => b.RaceId == decryptedRaceId && !b.AuditProperties.IsDeleted,
                    includeNavigationProperties: true)
                .Include(b => b.UploadedByUser)
                .OrderByDescending(b => b.AuditProperties.CreatedDate);

            var totalCount = await query.CountAsync();

            var batches = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            return new FileUploadBatchListDto
            {
                Batches = batches.Select(MapToStatusDto).ToList(),
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<List<FileUploadRecordDto>> GetBatchRecordsAsync(int batchId, int pageNumber = 1, int pageSize = 100)
        {
            var recordRepo = _repository.GetRepository<FileUploadRecord>();

            var records = await recordRepo.GetQuery(
                    r => r.FileUploadBatchId == batchId,
                    includeNavigationProperties: true)
                .Include(r => r.MatchedChip)
                .Include(r => r.MatchedParticipant)
                .OrderBy(r => r.RowNumber)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .Select(r => new FileUploadRecordDto
                {
                    Id = r.Id,
                    RowNumber = r.RowNumber,
                    Epc = r.Epc,
                    ReadTimestamp = r.ReadTimestamp,
                    AntennaPort = r.AntennaPort,
                    RssiDbm = r.RssiDbm,
                    Status = r.ProcessingStatus,
                    ErrorMessage = r.ErrorMessage,
                    MatchedChipId = r.MatchedChipId,
                    MatchedChipEpc = r.MatchedChip != null ? r.MatchedChip.EPC : null,
                    MatchedParticipantId = r.MatchedParticipantId,
                    MatchedParticipantName = r.MatchedParticipant != null
                        ? $"{r.MatchedParticipant.FirstName} {r.MatchedParticipant.LastName}"
                        : null,
                    MatchedBibNumber = r.MatchedParticipant != null ? r.MatchedParticipant.BibNumber : null
                })
                .ToListAsync();

            return records;
        }

        public async Task<bool> CancelBatchAsync(int batchId)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            var batch = await batchRepo.GetQuery(b => b.Id == batchId && !b.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();

            if (batch == null) return false;

            if (batch.ProcessingStatus == FileProcessingStatus.Processing)
            {
                batch.ProcessingStatus = FileProcessingStatus.Cancelled;
                await batchRepo.UpdateAsync(batch);
                await _repository.SaveChangesAsync();
                return true;
            }

            return false;
        }

        public async Task<bool> ReprocessBatchAsync(ReprocessBatchRequest request)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();
            var recordRepo = _repository.GetRepository<FileUploadRecord>();

            var batch = await batchRepo.GetQuery(b => b.Id == request.BatchId && !b.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();

            if (batch == null) return false;

            // Reset status
            batch.ProcessingStatus = FileProcessingStatus.Pending;
            batch.ProcessedRecords = 0;
            batch.MatchedRecords = 0;
            batch.ErrorRecords = 0;
            batch.DuplicateRecords = 0;
            batch.ErrorMessage = null;
            batch.ProcessingStartedAt = null;
            batch.ProcessingCompletedAt = null;

            if (request.ReprocessAll)
            {
                // Reset all records
                var records = await recordRepo.GetQuery(r => r.FileUploadBatchId == request.BatchId)
                    .ToListAsync();

                foreach (var record in records)
                {
                    record.ProcessingStatus = ReadRecordStatus.Pending;
                    record.ErrorMessage = null;
                    record.ProcessedAt = null;
                }

                await recordRepo.UpdateRangeAsync(records);
            }
            else if (request.ReprocessErrors)
            {
                // Reset error records - includes InvalidEpc, InvalidTimestamp, OutOfRaceWindow, UnknownChip
                var errorStatuses = new[]
                {
                    ReadRecordStatus.InvalidEpc,
                    ReadRecordStatus.InvalidTimestamp,
                    ReadRecordStatus.OutOfRaceWindow,
                    ReadRecordStatus.UnknownChip
                };

                var errorRecords = await recordRepo.GetQuery(
                        r => r.FileUploadBatchId == request.BatchId && errorStatuses.Contains(r.ProcessingStatus))
                    .ToListAsync();

                foreach (var record in errorRecords)
                {
                    record.ProcessingStatus = ReadRecordStatus.Pending;
                    record.ErrorMessage = null;
                    record.ProcessedAt = null;
                }

                await recordRepo.UpdateRangeAsync(errorRecords);
            }

            await batchRepo.UpdateAsync(batch);
            await _repository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteBatchAsync(int batchId)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();
            var recordRepo = _repository.GetRepository<FileUploadRecord>();

            var batch = await batchRepo.GetQuery(b => b.Id == batchId && !b.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();

            if (batch == null) return false;

            // Soft delete batch
            batch.AuditProperties.IsDeleted = true;
            batch.AuditProperties.IsActive = false;
            batch.AuditProperties.UpdatedDate = DateTime.UtcNow;

            // Also soft delete records
            var records = await recordRepo.GetQuery(r => r.FileUploadBatchId == batchId)
                .ToListAsync();

            foreach (var record in records)
            {
                record.AuditProperties.IsDeleted = true;
                record.AuditProperties.IsActive = false;
            }

            await batchRepo.UpdateAsync(batch);
            await recordRepo.UpdateRangeAsync(records);
            await _repository.SaveChangesAsync();

            // Optionally delete the physical file
            var filePath = Path.Combine(_uploadPath, batch.StoredFileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return true;
        }

        /// <inheritdoc />
        public async Task<FileUploadBatch?> GetBatchByIdAsync(int batchId)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            return await batchRepo.GetQuery(
                    b => b.Id == batchId && !b.AuditProperties.IsDeleted,
                    includeNavigationProperties: false)
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        #region Private Methods

        /// <summary>
        /// Detect file format based on filename and extension
        /// </summary>
        private static FileFormat DetectFileFormat(string fileName, string extension)
        {
            var lowerName = fileName.ToLowerInvariant();

            // ============================================
            // FIX: SQLite database file from R700 offline storage
            // File pattern: YYYY-MM-DD_<mac_address>.db
            // Example: 2025-11-22_00162512dbbf_.db
            // ============================================
            if (extension == ".db")
            {
                return FileFormat.ImpinjSqlite;
            }

            // Impinj R700 CSV/JSON files
            if (lowerName.Contains("impinj") || lowerName.Contains("r700"))
            {
                return extension == ".json" ? FileFormat.ImpinjJson : FileFormat.ImpinjCsv;
            }

            // Chronotrack files
            if (lowerName.Contains("chronotrack"))
            {
                return FileFormat.ChronotrackCsv;
            }

            // Default based on extension
            return extension == ".json" ? FileFormat.CustomJson : FileFormat.GenericCsv;
        }

        private static FileUploadStatusDto MapToStatusDto(FileUploadBatch batch)
        {
            return new FileUploadStatusDto
            {
                BatchId = batch.Id,
                BatchGuid = batch.BatchGuid,
                OriginalFileName = batch.OriginalFileName,
                Status = batch.ProcessingStatus,
                TotalRecords = batch.TotalRecords,
                ProcessedRecords = batch.ProcessedRecords,
                MatchedRecords = batch.MatchedRecords,
                DuplicateRecords = batch.DuplicateRecords,
                ErrorRecords = batch.ErrorRecords,
                ProcessingStartedAt = batch.ProcessingStartedAt,
                ProcessingCompletedAt = batch.ProcessingCompletedAt,
                ErrorMessage = batch.ErrorMessage,
                CreatedAt = batch.AuditProperties.CreatedDate,
                UploadedByUserName = batch.UploadedByUser != null
                    ? (batch.UploadedByUser.FirstName != null
                        ? $"{batch.UploadedByUser.FirstName} {batch.UploadedByUser.LastName}"
                        : batch.UploadedByUser.Email)
                    : null
            };
        }

        /// <summary>
        /// Attempts to parse the input as an integer. If parsing fails, attempts to decrypt and parse the result.
        /// Throws ArgumentException when neither parsing nor decryption produce a valid integer.
        /// </summary>
        private int TryParseOrDecrypt(string input, string inputName)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException($"{inputName} cannot be null or empty", inputName);

            if (int.TryParse(input, out var id))
                return id;

            try
            {
                var decrypted = _encryptionService.Decrypt(input);
                if (int.TryParse(decrypted, out id))
                    return id;

                _logger.LogDebug("Decrypted value for {InputName} did not parse as int", inputName);
                throw new ArgumentException($"Invalid {inputName} format");
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                _logger.LogDebug(ex, "Failed to parse or decrypt input for {InputName}", inputName);
                throw new ArgumentException($"Invalid {inputName} format", inputName, ex);
            }
        }

        /// <summary>
        /// Attempts to parse or decrypt a nullable string ID.
        /// Returns null if the input is null or empty.
        /// </summary>
        private int? TryParseOrDecryptNullable(string? input, string inputName)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            return TryParseOrDecrypt(input, inputName);
        }

        #endregion
    }
}
