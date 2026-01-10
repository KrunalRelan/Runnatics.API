using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    /// <summary>
    /// Background service that processes uploaded RFID files
    /// </summary>
    public class FileProcessingBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FileProcessingBackgroundService> _logger;
        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

        public FileProcessingBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<FileProcessingBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("File Processing Background Service starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingBatchesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in File Processing Background Service");
                }

                await Task.Delay(_pollInterval, stoppingToken);
            }

            _logger.LogInformation("File Processing Background Service stopping");
        }

        private async Task ProcessPendingBatchesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<RaceSyncDbContext>();
            var processingService = scope.ServiceProvider.GetRequiredService<IFileProcessingService>();

            // Get pending batches
            var pendingBatches = await context.FileUploadBatches
                .Where(b => b.ProcessingStatus == FileProcessingStatus.Pending &&
                           !b.AuditProperties.IsDeleted)
                .OrderBy(b => b.AuditProperties.CreatedDate)
                .Take(5) // Process up to 5 batches at a time
                .Select(b => b.Id)
                .ToListAsync(stoppingToken);

            foreach (var batchId in pendingBatches)
            {
                if (stoppingToken.IsCancellationRequested) break;

                _logger.LogInformation("Processing batch {BatchId}", batchId);

                try
                {
                    await processingService.ProcessBatchAsync(batchId, stoppingToken);
                    _logger.LogInformation("Completed processing batch {BatchId}", batchId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process batch {BatchId}", batchId);
                }
            }
        }
    }
}
