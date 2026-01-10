using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.FileUpload;
using Runnatics.Models.Client.Reader;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Service for reader device operations
    /// </summary>
    public class ReaderService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IReaderService
    {
        private readonly ILogger<ReaderService> _logger;

        public ReaderService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<ReaderService> logger) : base(repository)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<List<ReaderStatusDto>> GetAllReadersAsync()
        {
            _logger.LogDebug("Fetching all active readers");

            var readerRepo = _repository.GetRepository<ReaderDevice>();

            var readers = await readerRepo.GetQuery(
                    r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: true)
                .Include(r => r.HealthStatus)
                .Include(r => r.ReaderAntennas)
                .Include(r => r.ReaderAlerts.Where(a => !a.IsAcknowledged))
                .AsNoTracking()
                .ToListAsync();

            var result = readers.Select(MapToReaderStatusDto).ToList();

            _logger.LogDebug("Retrieved {Count} readers", result.Count);
            return result;
        }

        /// <inheritdoc />
        public async Task<ReaderStatusDto?> GetReaderByIdAsync(int id)
        {
            _logger.LogDebug("Fetching reader with ID {ReaderId}", id);

            var readerRepo = _repository.GetRepository<ReaderDevice>();

            var reader = await readerRepo.GetQuery(
                    r => r.Id == id,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: true)
                .Include(r => r.HealthStatus)
                .Include(r => r.ReaderAntennas)
                .Include(r => r.ReaderAlerts.Where(a => !a.IsAcknowledged))
                .Include(r => r.Checkpoint)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (reader == null)
            {
                _logger.LogWarning("Reader with ID {ReaderId} not found", id);
                return null;
            }

            var result = MapToReaderStatusDto(reader);
            result.CheckpointName = reader.Checkpoint?.Name;

            return result;
        }

        /// <inheritdoc />
        public async Task<List<ReaderAlertDto>> GetAlertsAsync(bool unacknowledgedOnly = true, int? readerId = null)
        {
            _logger.LogDebug("Fetching alerts. UnacknowledgedOnly: {UnacknowledgedOnly}, ReaderId: {ReaderId}",
                unacknowledgedOnly, readerId);

            var alertRepo = _repository.GetRepository<ReaderAlert>();

            IQueryable<ReaderAlert> query = alertRepo.GetQuery(
                    a => !a.AuditProperties.IsDeleted,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: true)
                .Include(a => a.ReaderDevice)
                .Include(a => a.AcknowledgedByUser);

            if (unacknowledgedOnly)
            {
                query = query.Where(a => !a.IsAcknowledged);
            }

            if (readerId.HasValue)
            {
                query = query.Where(a => a.ReaderDeviceId == readerId.Value);
            }

            var alerts = await query
                .OrderByDescending(a => a.AuditProperties.CreatedDate)
                .Take(100)
                .AsNoTracking()
                .Select(a => new ReaderAlertDto
                {
                    Id = a.Id,
                    ReaderDeviceId = a.ReaderDeviceId,
                    ReaderName = a.ReaderDevice.Hostname ?? a.ReaderDevice.SerialNumber,
                    AlertType = a.AlertType,
                    Severity = a.Severity,
                    Message = a.Message,
                    IsAcknowledged = a.IsAcknowledged,
                    AcknowledgedByUserName = a.AcknowledgedByUser != null
                        ? (a.AcknowledgedByUser.FirstName != null
                            ? $"{a.AcknowledgedByUser.FirstName} {a.AcknowledgedByUser.LastName}"
                            : a.AcknowledgedByUser.Email)
                        : null,
                    AcknowledgedAt = a.AcknowledgedAt,
                    CreatedAt = a.AuditProperties.CreatedDate
                })
                .ToListAsync();

            _logger.LogDebug("Retrieved {Count} alerts", alerts.Count);
            return alerts;
        }

        /// <inheritdoc />
        public async Task<bool> AcknowledgeAlertAsync(long alertId, int userId, string? resolutionNotes = null)
        {
            _logger.LogInformation("Acknowledging alert {AlertId} by user {UserId}", alertId, userId);

            var alertRepo = _repository.GetRepository<ReaderAlert>();

            var alert = await alertRepo.GetQuery(a => a.Id == alertId)
                .FirstOrDefaultAsync();

            if (alert == null)
            {
                _logger.LogWarning("Alert {AlertId} not found", alertId);
                return false;
            }

            alert.IsAcknowledged = true;
            alert.AcknowledgedByUserId = userId;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.ResolutionNotes = resolutionNotes;
            alert.AuditProperties.UpdatedBy = userId;
            alert.AuditProperties.UpdatedDate = DateTime.UtcNow;

            await alertRepo.UpdateAsync(alert);
            await _repository.SaveChangesAsync();

            _logger.LogInformation("Alert {AlertId} acknowledged successfully", alertId);
            return true;
        }

        /// <inheritdoc />
        public async Task<RfidDashboardDto> GetDashboardAsync()
        {
            _logger.LogDebug("Fetching RFID dashboard data");

            var readerRepo = _repository.GetRepository<ReaderDevice>();
            var batchRepo = _repository.GetRepository<FileUploadBatch>();
            var alertRepo = _repository.GetRepository<ReaderAlert>();

            // Get reader statistics
            var readers = await readerRepo.GetQuery(
                    r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: true)
                .Include(r => r.HealthStatus)
                .AsNoTracking()
                .ToListAsync();

            // Get upload statistics
            var pendingUploads = await batchRepo.CountAsync(
                b => b.ProcessingStatus == FileProcessingStatus.Pending && !b.AuditProperties.IsDeleted);

            var processingUploads = await batchRepo.CountAsync(
                b => b.ProcessingStatus == FileProcessingStatus.Processing && !b.AuditProperties.IsDeleted);

            // Get alert statistics
            var unacknowledgedAlerts = await alertRepo.CountAsync(
                a => !a.IsAcknowledged && !a.AuditProperties.IsDeleted);

            // Get recent data
            var recentAlerts = await GetRecentAlertsAsync();
            var recentUploads = await GetRecentUploadsAsync();

            var dashboard = new RfidDashboardDto
            {
                TotalReaders = readers.Count,
                OnlineReaders = readers.Count(r => r.HealthStatus?.IsOnline == true),
                OfflineReaders = readers.Count(r => r.HealthStatus?.IsOnline != true),
                TotalReadsToday = readers.Sum(r => r.HealthStatus?.TotalReadsToday ?? 0),
                PendingUploads = pendingUploads,
                ProcessingUploads = processingUploads,
                UnacknowledgedAlerts = unacknowledgedAlerts,
                RecentAlerts = recentAlerts,
                RecentUploads = recentUploads
            };

            _logger.LogDebug("Dashboard data retrieved. TotalReaders: {TotalReaders}, OnlineReaders: {OnlineReaders}",
                dashboard.TotalReaders, dashboard.OnlineReaders);

            return dashboard;
        }

        #region Private Methods

        private static ReaderStatusDto MapToReaderStatusDto(ReaderDevice r)
        {
            return new ReaderStatusDto
            {
                Id = r.Id,
                Name = r.Hostname ?? r.SerialNumber,
                SerialNumber = r.SerialNumber,
                IpAddress = r.IpAddress,
                IsOnline = r.HealthStatus != null && r.HealthStatus.IsOnline,
                LastHeartbeat = r.HealthStatus?.LastHeartbeat,
                CpuTemperatureCelsius = r.HealthStatus?.CpuTemperatureCelsius,
                FirmwareVersion = r.HealthStatus?.FirmwareVersion,
                TotalReadsToday = r.HealthStatus?.TotalReadsToday ?? 0,
                LastReadTimestamp = r.HealthStatus?.LastReadTimestamp,
                Antennas = r.ReaderAntennas.Select(a => new AntennaStatusDto
                {
                    Id = a.Id,
                    Port = a.AntennaPort,
                    Name = a.AntennaName,
                    IsEnabled = a.IsEnabled,
                    TxPowerCdBm = a.TxPowerCdBm,
                    Position = a.Position.ToString()
                }).ToList(),
                UnacknowledgedAlerts = r.ReaderAlerts.Count(a => !a.IsAcknowledged)
            };
        }

        private async Task<List<ReaderAlertDto>> GetRecentAlertsAsync()
        {
            var alertRepo = _repository.GetRepository<ReaderAlert>();

            return await alertRepo.GetQuery(
                    a => !a.AuditProperties.IsDeleted,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: true)
                .Include(a => a.ReaderDevice)
                .OrderByDescending(a => a.AuditProperties.CreatedDate)
                .Take(10)
                .AsNoTracking()
                .Select(a => new ReaderAlertDto
                {
                    Id = a.Id,
                    ReaderDeviceId = a.ReaderDeviceId,
                    ReaderName = a.ReaderDevice.Hostname ?? a.ReaderDevice.SerialNumber,
                    AlertType = a.AlertType,
                    Severity = a.Severity,
                    Message = a.Message,
                    IsAcknowledged = a.IsAcknowledged,
                    CreatedAt = a.AuditProperties.CreatedDate
                })
                .ToListAsync();
        }

        private async Task<List<FileUploadStatusDto>> GetRecentUploadsAsync()
        {
            var batchRepo = _repository.GetRepository<FileUploadBatch>();

            return await batchRepo.GetQuery(
                    b => !b.AuditProperties.IsDeleted,
                    ignoreQueryFilters: false,
                    includeNavigationProperties: false)
                .OrderByDescending(b => b.AuditProperties.CreatedDate)
                .Take(10)
                .AsNoTracking()
                .Select(b => new FileUploadStatusDto
                {
                    BatchId = b.Id,
                    BatchGuid = b.BatchGuid,
                    OriginalFileName = b.OriginalFileName,
                    Status = b.ProcessingStatus,
                    TotalRecords = b.TotalRecords,
                    ProcessedRecords = b.ProcessedRecords,
                    MatchedRecords = b.MatchedRecords,
                    CreatedAt = b.AuditProperties.CreatedDate
                })
                .ToListAsync();
        }

        #endregion
    }
}
