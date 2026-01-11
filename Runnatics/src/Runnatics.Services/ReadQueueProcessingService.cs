using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Background service that processes the read queue
    /// </summary>
    public class ReadQueueProcessingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReadQueueProcessingService> _logger;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(100);
        private readonly int _batchSize = 100;

        public ReadQueueProcessingService(
            IServiceProvider serviceProvider,
            ILogger<ReadQueueProcessingService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Read Queue Processing Service starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var processedCount = await ProcessQueueAsync(stoppingToken);

                    // If no records processed, wait longer
                    if (processedCount == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                    else
                    {
                        await Task.Delay(_pollInterval, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Read Queue Processing Service");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("Read Queue Processing Service stopping");
        }

        private async Task<int> ProcessQueueAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<RaceSyncDbContext>>();

            var queueRepo = unitOfWork.GetRepository<ReadQueueItem>();
            var chipRepo = unitOfWork.GetRepository<Chip>();
            var readRawRepo = unitOfWork.GetRepository<ReadRaw>();
            var raceRepo = unitOfWork.GetRepository<Race>();

            // Get pending reads from queue
            var pendingReads = await queueRepo.GetQuery(
                    r => r.ProcessingStatus == ReadRecordStatus.Pending &&
                         r.RetryCount < r.MaxRetries,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: false)
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.Id)
                .Take(_batchSize)
                .ToListAsync(stoppingToken);

            if (pendingReads.Count == 0) return 0;

            // Get chip lookup
            var epcs = pendingReads.Select(r => r.Epc.ToUpperInvariant()).Distinct().ToList();
            var chips = await chipRepo.GetQuery(
                    c => epcs.Contains(c.EPC.ToUpperInvariant()) &&
                         c.AuditProperties.IsActive &&
                         !c.AuditProperties.IsDeleted,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: false)
                .ToDictionaryAsync(c => c.EPC.ToUpperInvariant(), c => c, stoppingToken);

            int processedCount = 0;

            foreach (var queueItem in pendingReads)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var epcUpper = queueItem.Epc.ToUpperInvariant();

                    if (!chips.TryGetValue(epcUpper, out var chip))
                    {
                        queueItem.ProcessingStatus = ReadRecordStatus.UnknownChip;
                        queueItem.ErrorMessage = "Chip not found";
                        queueItem.ProcessedAt = DateTime.UtcNow;
                        processedCount++;
                        continue;
                    }

                    // Check for duplicate
                    var isDuplicate = await readRawRepo.GetQuery(
                            r => r.Epc == queueItem.Epc &&
                                 r.ReadTimestamp == queueItem.ReadTimestamp &&
                                 r.CheckpointId == queueItem.CheckpointId,
                            ignoreQueryFilters: false,
                            includeNavigationProperties: false)
                        .AnyAsync(stoppingToken);

                    if (isDuplicate)
                    {
                        queueItem.ProcessingStatus = ReadRecordStatus.Duplicate;
                        queueItem.ErrorMessage = "Duplicate read";
                        queueItem.ProcessedAt = DateTime.UtcNow;
                        processedCount++;
                        continue;
                    }

                    // Create ReadRaw - need EventId, use RaceId to look up
                    var eventId = queueItem.RaceId.HasValue
                        ? await raceRepo.GetQuery(
                                r => r.Id == queueItem.RaceId,
                                ignoreQueryFilters: false,
                                includeNavigationProperties: false)
                            .Select(r => r.EventId)
                            .FirstOrDefaultAsync(stoppingToken)
                        : 0;

                    if (eventId == 0)
                    {
                        queueItem.ProcessingStatus = ReadRecordStatus.InvalidEpc;
                        queueItem.ErrorMessage = "Could not determine event for race";
                        queueItem.ProcessedAt = DateTime.UtcNow;
                        processedCount++;
                        continue;
                    }

                    var readRaw = new ReadRaw
                    {
                        EventId = eventId,
                        ChipEPC = queueItem.Epc,
                        Epc = queueItem.Epc,
                        ReaderDeviceId = queueItem.ReaderDeviceId ?? 0,
                        CheckpointId = queueItem.CheckpointId,
                        Timestamp = queueItem.ReadTimestamp,
                        ReadTimestamp = queueItem.ReadTimestamp,
                        AntennaPort = queueItem.AntennaPort,
                        Rssi = queueItem.RssiDbm.HasValue ? (int)queueItem.RssiDbm.Value : null,
                        Source = queueItem.Source,
                        FileUploadBatchId = queueItem.FileUploadBatchId,
                        IsProcessed = false,
                        AuditProperties = new AuditProperties
                        {
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };

                    await readRawRepo.AddAsync(readRaw);

                    queueItem.ProcessingStatus = ReadRecordStatus.Processed;
                    queueItem.ProcessedAt = DateTime.UtcNow;
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing queue item {Id}", queueItem.Id);
                    queueItem.RetryCount++;
                    queueItem.ErrorMessage = ex.Message;
                }
            }

            await unitOfWork.SaveChangesAsync();

            if (processedCount > 0)
            {
                _logger.LogDebug("Processed {Count} queue items", processedCount);
            }

            return processedCount;
        }
    }
}
