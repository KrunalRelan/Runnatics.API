using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var context = scope.ServiceProvider.GetRequiredService<RunnaticsDbContext>();

            // Get pending reads from queue
            var pendingReads = await context.ReadQueue
                .Where(r => r.ProcessingStatus == ReadRecordStatus.Pending &&
                           r.RetryCount < r.MaxRetries)
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.Id)
                .Take(_batchSize)
                .ToListAsync(stoppingToken);

            if (pendingReads.Count == 0) return 0;

            // Get chip lookup
            var epcs = pendingReads.Select(r => r.Epc.ToUpperInvariant()).Distinct().ToList();
            var chips = await context.Chips
                .Where(c => epcs.Contains(c.Epc.ToUpperInvariant()) &&
                           c.AuditProperties.IsActive &&
                           !c.AuditProperties.IsDeleted)
                .ToDictionaryAsync(c => c.Epc.ToUpperInvariant(), c => c, stoppingToken);

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
                    var isDuplicate = await context.ReadRaws
                        .AnyAsync(r => r.Epc == queueItem.Epc &&
                                      r.ReadTimestamp == queueItem.ReadTimestamp &&
                                      r.CheckpointId == queueItem.CheckpointId, stoppingToken);

                    if (isDuplicate)
                    {
                        queueItem.ProcessingStatus = ReadRecordStatus.Duplicate;
                        queueItem.ErrorMessage = "Duplicate read";
                        queueItem.ProcessedAt = DateTime.UtcNow;
                        processedCount++;
                        continue;
                    }

                    // Create ReadRaw
                    var readRaw = new ReadRaw
                    {
                        Epc = queueItem.Epc,
                        ChipId = chip.Id,
                        ReaderDeviceId = queueItem.ReaderDeviceId,
                        CheckpointId = queueItem.CheckpointId,
                        RaceId = queueItem.RaceId,
                        ReadTimestamp = queueItem.ReadTimestamp,
                        AntennaPort = queueItem.AntennaPort,
                        RssiDbm = queueItem.RssiDbm,
                        Source = queueItem.Source,
                        FileUploadBatchId = queueItem.FileUploadBatchId,
                        AuditProperties = new AuditProperties
                        {
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };

                    context.ReadRaws.Add(readRaw);

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

            await context.SaveChangesAsync(stoppingToken);

            if (processedCount > 0)
            {
                _logger.LogDebug("Processed {Count} queue items", processedCount);
            }

            return processedCount;
        }
    }
}
