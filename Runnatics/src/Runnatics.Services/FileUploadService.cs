using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Common;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    public class FileUploadService : IFileUploadService
    {
       private readonly IUnitOfWork<RaceSyncDbContext> repository;
        private readonly ILogger<FileUploadService> _logger;
        private readonly IFileParserFactory _parserFactory;
        private readonly string _uploadPath;

        public FileUploadService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<FileUploadService> logger,
            IFileParserFactory parserFactory,
            IConfiguration configuration)
        {
            this.repository = repository;
            _logger = logger;
            _parserFactory = parserFactory;
            _uploadPath = configuration["FileUpload:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
            }
        }

        public async Task<FileUploadResponse> UploadFileAsync(IFormFile file, FileUploadRequest request, int userId)
        {
            // Validate file
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("No file uploaded");
            }

            var allowedExtensions = new[] { ".csv", ".json", ".txt" };
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
            using (var md5 = MD5.Create())
            {
                using var stream = file.OpenReadStream();
                var hash = await md5.ComputeHashAsync(stream);
                fileHash = Convert.ToHexString(hash);
            }

            // Check for duplicate file
            var existingBatch = await _context.FileUploadBatches
                .FirstOrDefaultAsync(b => b.FileHash == fileHash &&
                                         b.RaceId == request.RaceId &&
                                         !b.AuditProperties.IsDeleted);

            if (existingBatch != null)
            {
                _logger.LogWarning("Duplicate file detected: {FileName} matches batch {BatchId}",
                    file.FileName, existingBatch.Id);

                return new FileUploadResponse
                {
                    BatchId = existingBatch.Id,
                    BatchGuid = existingBatch.BatchGuid,
                    FileName = file.FileName,
                    FileSizeBytes = file.Length,
                    DetectedFormat = existingBatch.FileFormat,
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
                RaceId = request.RaceId,
                EventId = request.EventId,
                ReaderDeviceId = request.ReaderDeviceId,
                CheckpointId = request.CheckpointId,
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

            _context.FileUploadBatches.Add(batch);
            await _context.SaveChangesAsync();

            _logger.LogInformation("File uploaded successfully: {FileName} -> Batch {BatchId}",
                file.FileName, batch.Id);

            return new FileUploadResponse
            {
                BatchId = batch.Id,
                BatchGuid = batch.BatchGuid,
                FileName = file.FileName,
                FileSizeBytes = file.Length,
                DetectedFormat = detectedFormat,
                Status = FileProcessingStatus.Pending,
                Message = "File uploaded successfully. Processing will begin shortly."
            };
        }

        public async Task<FileUploadStatusDto> GetBatchStatusAsync(int batchId)
        {
            var batch = await _context.FileUploadBatches
                .Include(b => b.UploadedByUser)
                .FirstOrDefaultAsync(b => b.Id == batchId && !b.AuditProperties.IsDeleted);

            if (batch == null)
            {
                throw new KeyNotFoundException($"Batch {batchId} not found");
            }

            return MapToStatusDto(batch);
        }

        public async Task<FileUploadStatusDto?> GetBatchStatusByGuidAsync(Guid batchGuid)
        {
            var batch = await _context.FileUploadBatches
                .Include(b => b.UploadedByUser)
                .FirstOrDefaultAsync(b => b.BatchGuid == batchGuid && !b.AuditProperties.IsDeleted);

            return batch == null ? null : MapToStatusDto(batch);
        }

        public async Task<FileUploadBatchListDto> GetBatchesAsync(int raceId, int pageNumber = 1, int pageSize = 20)
        {
            var query = _context.FileUploadBatches
                .Include(b => b.UploadedByUser)
                .Where(b => b.RaceId == raceId && !b.AuditProperties.IsDeleted)
                .OrderByDescending(b => b.AuditProperties.CreatedDate);

            var totalCount = await query.CountAsync();

            var batches = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
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
            var records = await _context.FileUploadRecords
                .Include(r => r.MatchedChip)
                .Include(r => r.MatchedParticipant)
                .Where(r => r.FileUploadBatchId == batchId)
                .OrderBy(r => r.RowNumber)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
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
                    MatchedChipCode = r.MatchedChip != null ? r.MatchedChip.ChipCode : null,
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
            var batch = await _context.FileUploadBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && !b.AuditProperties.IsDeleted);

            if (batch == null) return false;

            if (batch.ProcessingStatus == FileProcessingStatus.Processing)
            {
                batch.ProcessingStatus = FileProcessingStatus.Cancelled;
                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        }

        public async Task<bool> ReprocessBatchAsync(ReprocessBatchRequest request)
        {
            var batch = await _context.FileUploadBatches
                .FirstOrDefaultAsync(b => b.Id == request.BatchId && !b.AuditProperties.IsDeleted);

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
                var records = await _context.FileUploadRecords
                    .Where(r => r.FileUploadBatchId == request.BatchId)
                    .ToListAsync();

                foreach (var record in records)
                {
                    record.ProcessingStatus = ReadRecordStatus.Pending;
                    record.ErrorMessage = null;
                    record.ProcessedAt = null;
                }
            }
            else if (request.ReprocessErrors)
            {
                // Reset only error records
                var errorRecords = await _context.FileUploadRecords
                    .Where(r => r.FileUploadBatchId == request.BatchId &&
                               (r.ProcessingStatus == ReadRecordStatus.InvalidEpc ||
                                r.ProcessingStatus == ReadRecordStatus.InvalidTimestamp))
                    .ToListAsync();

                foreach (var record in errorRecords)
                {
                    record.ProcessingStatus = ReadRecordStatus.Pending;
                    record.ErrorMessage = null;
                    record.ProcessedAt = null;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteBatchAsync(int batchId)
        {
            var batch = await _context.FileUploadBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && !b.AuditProperties.IsDeleted);

            if (batch == null) return false;

            // Soft delete
            batch.AuditProperties.IsDeleted = true;
            batch.AuditProperties.IsActive = false;
            batch.AuditProperties.UpdatedDate = DateTime.UtcNow;

            // Also soft delete records
            var records = await _context.FileUploadRecords
                .Where(r => r.FileUploadBatchId == batchId)
                .ToListAsync();

            foreach (var record in records)
            {
                record.AuditProperties.IsDeleted = true;
                record.AuditProperties.IsActive = false;
            }

            await _context.SaveChangesAsync();

            // Optionally delete the physical file
            var filePath = Path.Combine(_uploadPath, batch.StoredFileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return true;
        }

        private UploadFileFormat DetectFileFormat(string fileName, string extension)
        {
            var lowerName = fileName.ToLowerInvariant();

            if (lowerName.Contains("impinj") || lowerName.Contains("r700"))
            {
                return extension == ".json" ? UploadFileFormat.ImpinjJson : UploadFileFormat.ImpinjCsv;
            }

            if (lowerName.Contains("chronotrack"))
            {
                return UploadFileFormat.ChronotrackCsv;
            }

            return extension == ".json" ? UploadFileFormat.CustomJson : UploadFileFormat.GenericCsv;
        }

        private FileUploadStatusDto MapToStatusDto(FileUploadBatch batch)
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
                UploadedByUserName = batch.UploadedByUser?.UserName
            };
        }
    }
}
