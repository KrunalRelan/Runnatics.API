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
    /// Background service for reader health monitoring
    /// </summary>
    public class ReaderHealthMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReaderHealthMonitorService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _offlineThreshold = TimeSpan.FromMinutes(2);

        public ReaderHealthMonitorService(
            IServiceProvider serviceProvider,
            ILogger<ReaderHealthMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Reader Health Monitor Service starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckReaderHealthAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Reader Health Monitor Service");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Reader Health Monitor Service stopping");
        }

        private async Task CheckReaderHealthAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<RaceSyncDbContext>>();

            var healthStatusRepo = unitOfWork.GetRepository<ReaderHealthStatus>();
            var alertRepo = unitOfWork.GetRepository<ReaderAlert>();
            var connectionLogRepo = unitOfWork.GetRepository<ReaderConnectionLog>();

            var now = DateTime.UtcNow;
            var offlineThreshold = now - _offlineThreshold;

            // Get readers that haven't sent heartbeat
            var offlineReaders = await healthStatusRepo.GetQuery(
                    h => h.IsOnline &&
                         h.LastHeartbeat.HasValue &&
                         h.LastHeartbeat.Value < offlineThreshold,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: true)
                .Include(h => h.ReaderDevice)
                .ToListAsync(stoppingToken);

            foreach (var healthStatus in offlineReaders)
            {
                _logger.LogWarning("Reader {ReaderId} appears offline - last heartbeat: {LastHeartbeat}",
                    healthStatus.ReaderDeviceId, healthStatus.LastHeartbeat);

                // Update status
                healthStatus.IsOnline = false;
                healthStatus.ReaderMode = ReaderMode.Offline;
                healthStatus.AuditProperties.UpdatedDate = now;

                await healthStatusRepo.UpdateAsync(healthStatus);

                // Create alert
                var alert = new ReaderAlert
                {
                    ReaderDeviceId = healthStatus.ReaderDeviceId,
                    AlertType = ReaderAlertType.Offline,
                    Severity = AlertSeverity.Critical,
                    Message = $"Reader has not sent heartbeat since {healthStatus.LastHeartbeat:yyyy-MM-dd HH:mm:ss}",
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = now,
                        IsActive = true,
                        IsDeleted = false
                    }
                };

                await alertRepo.AddAsync(alert);

                // Log connection event
                var connectionLog = new ReaderConnectionLog
                {
                    ReaderDeviceId = healthStatus.ReaderDeviceId,
                    EventType = ReaderConnectionEventType.Disconnected,
                    Timestamp = now,
                    ErrorMessage = "Heartbeat timeout",
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = now,
                        IsActive = true,
                        IsDeleted = false
                    }
                };

                await connectionLogRepo.AddAsync(connectionLog);
            }

            if (offlineReaders.Any())
            {
                await unitOfWork.SaveChangesAsync();
            }
        }
    }
}
