using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    public class FileProcessingService : IFileProcessingService
    {
        private readonly IUnitOfWork<RaceSyncDbContext> _context;
        private readonly ILogger<FileProcessingService> _logger;
        private readonly IFileParserFactory _parserFactory;
        private readonly string _uploadPath;

        public FileProcessingService(
            IUnitOfWork<RaceSyncDbContext> context,
            ILogger<FileProcessingService> logger,
            IFileParserFactory parserFactory,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _parserFactory = parserFactory;
            _uploadPath = configuration["FileUpload:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        }

        public async Task ProcessBatchAsync(int batchId, CancellationToken cancellationToken = default)
        {
            var batch = await _context.FileUploadBatches
                .Include(b => b.Race)
                .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

            if (batch == null)
            {
                _logger.LogError("Batch {BatchId} not found", batchId);
                return;
            }

            if (batch.ProcessingStatus == FileProcessingStatus.Processing)
            {
                _logger.LogWarning("Batch {BatchId} is already being processed", batchId);
                return;
            }

            try
            {
                // Update status to processing
                batch.ProcessingStatus = FileProcessingStatus.Processing;
                batch.ProcessingStartedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                // Parse file
                var tagReads = await ParseFileAsync(batchId);
                batch.TotalRecords = tagReads.Count;

                _logger.LogInformation("Parsed {Count} records from batch {BatchId}", tagReads.Count, batchId);

                // Get mapping for chip lookup
                var chipLookup = await _context.Chips
                    .Where(c => c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                    .ToDictionaryAsync(c => c.Epc.ToUpperInvariant(), c => c, cancellationToken);

                // Get chip assignments for participant lookup
                var chipAssignments = await _context.ChipAssignments
                    .Include(ca => ca.Participant)
                    .Where(ca => ca.RaceId == batch.RaceId &&
                                ca.AuditProperties.IsActive &&
                                !ca.AuditProperties.IsDeleted)
                    .ToDictionaryAsync(ca => ca.ChipId, ca => ca, cancellationToken);

                // Get checkpoint and reader info
                var checkpoint = batch.CheckpointId.HasValue
                    ? await _context.Checkpoints.FindAsync(batch.CheckpointId.Value)
                    : null;

                // Process each record
                int rowNumber = 0;
                foreach (var tagRead in tagReads)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        batch.ProcessingStatus = FileProcessingStatus.Cancelled;
                        break;
                    }

                    rowNumber++;
                    var record = new FileUploadRecord
                    {
                        FileUploadBatchId = batchId,
                        RowNumber = rowNumber,
                        Epc = tagRead.Epc,
                        ReadTimestamp = tagRead.Timestamp,
                        AntennaPort = (byte?)tagRead.AntennaPort,
                        RssiDbm = (decimal?)tagRead.RssiDbm,
                        ReaderSerialNumber = tagRead.ReaderSerialNumber,
                        ReaderHostname = tagRead.ReaderHostname,
                        PhaseAngleDegrees = (decimal?)tagRead.PhaseAngleDegrees,
                        DopplerFrequencyHz = (decimal?)tagRead.DopplerFrequencyHz,
                        ChannelIndex = tagRead.ChannelIndex,
                        PeakRssiCdBm = tagRead.PeakRssiCdBm,
                        TagSeenCount = tagRead.TagSeenCount,
                        GpsLatitude = (decimal?)tagRead.GpsLatitude,
                        GpsLongitude = (decimal?)tagRead.GpsLongitude,
                        AuditProperties = new AuditProperties
                        {
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };

                    // Validate EPC
                    if (string.IsNullOrWhiteSpace(tagRead.Epc))
                    {
                        record.ProcessingStatus = ReadRecordStatus.InvalidEpc;
                        record.ErrorMessage = "Empty EPC";
                        batch.ErrorRecords++;
                        _context.FileUploadRecords.Add(record);
                        continue;
                    }

                    // Validate timestamp
                    if (tagRead.Timestamp == default || tagRead.Timestamp < new DateTime(2020, 1, 1))
                    {
                        record.ProcessingStatus = ReadRecordStatus.InvalidTimestamp;
                        record.ErrorMessage = "Invalid timestamp";
                        batch.ErrorRecords++;
                        _context.FileUploadRecords.Add(record);
                        continue;
                    }

                    // Check if timestamp is within race window
                    if (batch.Race != null)
                    {
                        var raceStart = batch.Race.StartTime?.AddHours(-1); // 1 hour buffer
                        var raceEnd = batch.Race.EndTime?.AddHours(2); // 2 hour buffer

                        if ((raceStart.HasValue && tagRead.Timestamp < raceStart) ||
                            (raceEnd.HasValue && tagRead.Timestamp > raceEnd))
                        {
                            record.ProcessingStatus = ReadRecordStatus.OutOfRaceWindow;
                            record.ErrorMessage = "Timestamp outside race window";
                            batch.ErrorRecords++;
                            _context.FileUploadRecords.Add(record);
                            continue;
                        }
                    }

                    // Look up chip
                    var epcUpper = tagRead.Epc.ToUpperInvariant();
                    if (chipLookup.TryGetValue(epcUpper, out var chip))
                    {
                        record.MatchedChipId = chip.Id;

                        // Look up participant
                        if (chipAssignments.TryGetValue(chip.Id, out var assignment))
                        {
                            record.MatchedParticipantId = assignment.ParticipantId;
                            batch.MatchedRecords++;
                        }
                    }
                    else
                    {
                        record.ProcessingStatus = ReadRecordStatus.UnknownChip;
                        record.ErrorMessage = "Chip not found in system";
                        // Still add to records, might be useful for debugging
                    }

                    // Check for duplicates
                    var isDuplicate = await _context.ReadRaws
                        .AnyAsync(r => r.Epc == tagRead.Epc &&
                                      r.ReadTimestamp == tagRead.Timestamp &&
                                      r.CheckpointId == batch.CheckpointId, cancellationToken);

                    if (isDuplicate)
                    {
                        record.ProcessingStatus = ReadRecordStatus.Duplicate;
                        record.ErrorMessage = "Duplicate read already exists";
                        batch.DuplicateRecords++;
                        _context.FileUploadRecords.Add(record);
                        continue;
                    }

                    // Create ReadRaw record
                    if (record.MatchedChipId.HasValue)
                    {
                        var readRaw = new ReadRaw
                        {
                            Epc = tagRead.Epc,
                            ChipId = record.MatchedChipId,
                            ReaderDeviceId = batch.ReaderDeviceId,
                            CheckpointId = batch.CheckpointId,
                            RaceId = batch.RaceId,
                            ReadTimestamp = tagRead.Timestamp,
                            AntennaPort = (byte?)tagRead.AntennaPort,
                            RssiDbm = (decimal?)tagRead.RssiDbm,
                            Source = "FileUpload",
                            FileUploadBatchId = batchId,
                            AuditProperties = new AuditProperties
                            {
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        _context.ReadRaws.Add(readRaw);
                        await _context.SaveChangesAsync(cancellationToken);

                        record.CreatedReadRawId = readRaw.Id;
                        record.ProcessingStatus = ReadRecordStatus.Processed;
                    }
                    else
                    {
                        record.ProcessingStatus = ReadRecordStatus.Valid;
                    }

                    record.ProcessedAt = DateTime.UtcNow;
                    batch.ProcessedRecords++;
                    _context.FileUploadRecords.Add(record);

                    // Save in batches of 100
                    if (rowNumber % 100 == 0)
                    {
                        await _context.SaveChangesAsync(cancellationToken);
                        _logger.LogDebug("Processed {Count}/{Total} records for batch {BatchId}",
                            rowNumber, batch.TotalRecords, batchId);
                    }
                }

                // Final save
                await _context.SaveChangesAsync(cancellationToken);

                // Update batch status
                batch.ProcessingStatus = batch.ErrorRecords > 0 && batch.ProcessedRecords > 0
                    ? FileProcessingStatus.PartiallyCompleted
                    : batch.ErrorRecords == batch.TotalRecords
                        ? FileProcessingStatus.Failed
                        : FileProcessingStatus.Completed;

                batch.ProcessingCompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Completed processing batch {BatchId}: {Processed} processed, {Matched} matched, {Duplicates} duplicates, {Errors} errors",
                    batchId, batch.ProcessedRecords, batch.MatchedRecords, batch.DuplicateRecords, batch.ErrorRecords);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch {BatchId}", batchId);
                batch.ProcessingStatus = FileProcessingStatus.Failed;
                batch.ErrorMessage = ex.Message;
                batch.ProcessingCompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<List<ImpinjTagRead>> ParseFileAsync(int batchId)
        {
            var batch = await _context.FileUploadBatches
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
            {
                throw new KeyNotFoundException($"Batch {batchId} not found");
            }

            var filePath = Path.Combine(_uploadPath, batch.StoredFileName);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {batch.StoredFileName}");
            }

            // Get mapping configuration
            var mapping = await _context.FileUploadMappings
                .FirstOrDefaultAsync(m => m.FileFormat == batch.FileFormat && m.IsDefault);

            var parser = _parserFactory.GetParser(batch.FileFormat);

            using var stream = File.OpenRead(filePath);
            return await parser.ParseAsync(stream, mapping);
        }
    }
}
