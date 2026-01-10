using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Service for processing uploaded files
    /// </summary>
    public class FileProcessingService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IFileProcessingService
    {
        private readonly ILogger<FileProcessingService> _logger;
        private readonly IFileParserFactory _parserFactory;
        private readonly IRaceNotificationService? _notificationService;
        private readonly string _uploadPath;

        public FileProcessingService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<FileProcessingService> logger,
            IFileParserFactory parserFactory,
            IConfiguration configuration,
            IRaceNotificationService? notificationService = null) : base(repository)
        {
            _logger = logger;
            _parserFactory = parserFactory;
            _notificationService = notificationService;
            _uploadPath = configuration["FileUpload:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        }

        public async Task ProcessBatchAsync(int batchId, CancellationToken cancellationToken = default)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();
            var recordRepo = _repository.GetRepository<FileUploadRecord>();
            var readRawRepo = _repository.GetRepository<ReadRaw>();
            var chipRepo = _repository.GetRepository<Chip>();
            var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
            var checkpointRepo = _repository.GetRepository<Checkpoint>();

            var batch = await batchRepo.GetQuery(b => b.Id == batchId, includeNavigationProperties: true)
                .Include(b => b.Race)
                .FirstOrDefaultAsync(cancellationToken);

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
                await batchRepo.UpdateAsync(batch);
                await _repository.SaveChangesAsync();

                // Parse file
                var tagReads = await ParseFileAsync(batchId);
                batch.TotalRecords = tagReads.Count;

                _logger.LogInformation("Parsed {Count} records from batch {BatchId}", tagReads.Count, batchId);

                // Get mapping for chip lookup
                var chips = await chipRepo.GetQuery(
                        c => c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                var chipLookup = chips.ToDictionary(c => c.EPC.ToUpperInvariant(), c => c);

                // Get chip assignments for participant lookup
                var assignments = await chipAssignmentRepo.GetQuery(
                        ca => ca.AuditProperties.IsActive && !ca.AuditProperties.IsDeleted,
                        includeNavigationProperties: true)
                    .Include(ca => ca.Participant)
                    .Include(ca => ca.Event)
                    .Where(ca => ca.Event.Id == batch.Race!.EventId)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                var chipAssignments = assignments.ToDictionary(ca => ca.ChipId, ca => ca);

                // Get checkpoint info
                Checkpoint? checkpoint = null;
                if (batch.CheckpointId.HasValue)
                {
                    checkpoint = await checkpointRepo.GetByIdAsync(batch.CheckpointId.Value);
                }

                // Get EventId from Race
                var eventId = batch.Race?.EventId ?? 0;
                if (eventId == 0)
                {
                    throw new InvalidOperationException("Could not determine event for batch");
                }

                // Process each record
                int rowNumber = 0;
                var recordsToAdd = new List<FileUploadRecord>();
                var readRawsToAdd = new List<ReadRaw>();

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
                        record.ProcessingStatus = ReadRecordStatus.Error;
                        record.ErrorMessage = "Empty EPC";
                        batch.ErrorRecords++;
                        recordsToAdd.Add(record);
                        continue;
                    }

                    // Validate timestamp
                    if (tagRead.Timestamp == default || tagRead.Timestamp < new DateTime(2020, 1, 1))
                    {
                        record.ProcessingStatus = ReadRecordStatus.Error;
                        record.ErrorMessage = "Invalid timestamp";
                        batch.ErrorRecords++;
                        recordsToAdd.Add(record);
                        continue;
                    }

                    // Check if timestamp is within race window
                    if (batch.Race != null)
                    {
                        var raceStart = batch.Race.StartTime?.AddHours(-1);
                        var raceEnd = batch.Race.EndTime?.AddHours(2);

                        if ((raceStart.HasValue && tagRead.Timestamp < raceStart) ||
                            (raceEnd.HasValue && tagRead.Timestamp > raceEnd))
                        {
                            record.ProcessingStatus = ReadRecordStatus.Error;
                            record.ErrorMessage = "Timestamp outside race window";
                            batch.ErrorRecords++;
                            recordsToAdd.Add(record);
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
                    }

                    // Check for duplicates
                    var isDuplicate = await readRawRepo.GetQuery(
                            r => r.Epc == tagRead.Epc &&
                                 r.ReadTimestamp == tagRead.Timestamp &&
                                 r.CheckpointId == batch.CheckpointId)
                        .AnyAsync(cancellationToken);

                    if (isDuplicate)
                    {
                        record.ProcessingStatus = ReadRecordStatus.Duplicate;
                        record.ErrorMessage = "Duplicate read already exists";
                        batch.DuplicateRecords++;
                        recordsToAdd.Add(record);
                        continue;
                    }

                    // Create ReadRaw record
                    if (record.MatchedChipId.HasValue)
                    {
                        var readRaw = new ReadRaw
                        {
                            EventId = eventId,
                            ChipEPC = tagRead.Epc,
                            Epc = tagRead.Epc,
                            ReaderDeviceId = batch.ReaderDeviceId ?? 0,
                            CheckpointId = batch.CheckpointId,
                            Timestamp = tagRead.Timestamp,
                            ReadTimestamp = tagRead.Timestamp,
                            AntennaPort = tagRead.AntennaPort,
                            Rssi = tagRead.RssiDbm.HasValue ? (int)tagRead.RssiDbm.Value : null,
                            Source = "FileUpload",
                            FileUploadBatchId = batchId,
                            IsProcessed = false,
                            AuditProperties = new AuditProperties
                            {
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        readRawsToAdd.Add(readRaw);
                        record.ProcessingStatus = ReadRecordStatus.Processed;
                    }
                    else
                    {
                        record.ProcessingStatus = ReadRecordStatus.Matched;
                    }

                    record.ProcessedAt = DateTime.UtcNow;
                    batch.ProcessedRecords++;
                    recordsToAdd.Add(record);

                    // Save in batches of 100 and send progress notification
                    if (rowNumber % 100 == 0)
                    {
                        if (readRawsToAdd.Count > 0)
                        {
                            await readRawRepo.AddRangeAsync(readRawsToAdd);
                            readRawsToAdd.Clear();
                        }
                        await recordRepo.AddRangeAsync(recordsToAdd);
                        recordsToAdd.Clear();
                        await batchRepo.UpdateAsync(batch);
                        await _repository.SaveChangesAsync();

                        _logger.LogDebug("Processed {Count}/{Total} records for batch {BatchId}",
                            rowNumber, batch.TotalRecords, batchId);

                        if (_notificationService != null)
                        {
                            await _notificationService.NotifyFileProcessingProgressAsync(batch);
                        }
                    }
                }

                // Final save for remaining records
                if (readRawsToAdd.Count > 0)
                {
                    await readRawRepo.AddRangeAsync(readRawsToAdd);
                }
                if (recordsToAdd.Count > 0)
                {
                    await recordRepo.AddRangeAsync(recordsToAdd);
                }
                await _repository.SaveChangesAsync();

                // Update batch status
                batch.ProcessingStatus = batch.ErrorRecords > 0 && batch.ProcessedRecords > 0
                    ? FileProcessingStatus.PartiallyCompleted
                    : batch.ErrorRecords == batch.TotalRecords
                        ? FileProcessingStatus.Failed
                        : FileProcessingStatus.Completed;

                batch.ProcessingCompletedAt = DateTime.UtcNow;
                await batchRepo.UpdateAsync(batch);
                await _repository.SaveChangesAsync();

                if (_notificationService != null)
                {
                    await _notificationService.NotifyFileProcessingCompleteAsync(batch);
                }

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
                await batchRepo.UpdateAsync(batch);
                await _repository.SaveChangesAsync();

                if (_notificationService != null)
                {
                    await _notificationService.NotifyFileProcessingCompleteAsync(batch);
                }

                throw;
            }
        }

        public async Task<List<ImpinjTagRead>> ParseFileAsync(int batchId)
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();
            var mappingRepo = _repository.GetRepository<FileUploadMapping>();

            var batch = await batchRepo.GetQuery(b => b.Id == batchId)
                .AsNoTracking()
                .FirstOrDefaultAsync();

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
            var mapping = await mappingRepo.GetQuery(
                    m => m.FileFormat == batch.FileFormat && m.IsDefault)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var parser = await _parserFactory.GetParser(batch.FileFormat);

            using var stream = File.OpenRead(filePath);
            return await parser.ParseAsync(stream, mapping);
        }
    }
}
