using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Runnatics.Services.Interface;
using Runnatics.Services.Interface.Hubs;
using DataEntities = Runnatics.Models.Data.Entities;

namespace Runnatics.Services
{
    /// <summary>
    /// Service for sending real-time notifications via SignalR
    /// </summary>
    public class RaceNotificationService : IRaceNotificationService
    {
        private readonly IHubContext<Hub<IRaceHubClient>, IRaceHubClient> _hubContext;
        private readonly ILogger<RaceNotificationService> _logger;

        public RaceNotificationService(
            IHubContext<Hub<IRaceHubClient>, IRaceHubClient> hubContext,
            ILogger<RaceNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task NotifyFileUploadedAsync(DataEntities.FileUploadBatch batch, string uploadedByUserName)
        {
            try
            {
                var notification = new FileUploadedNotification(
                    batch.Id,
                    batch.BatchGuid,
                    batch.OriginalFileName,
                    batch.RaceId,
                    uploadedByUserName,
                    batch.AuditProperties.CreatedDate
                );

                var groupName = SignalRGroupNames.GetRaceGroupName(batch.RaceId);
                await _hubContext.Clients.Group(groupName).FileUploaded(notification);

                _logger.LogDebug("Sent FileUploaded notification for batch {BatchId} to race {RaceId}", 
                    batch.Id, batch.RaceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send FileUploaded notification for batch {BatchId}", batch.Id);
            }
        }

        public async Task NotifyFileProcessingProgressAsync(DataEntities.FileUploadBatch batch)
        {
            try
            {
                var progressPercent = batch.TotalRecords > 0 
                    ? (double)batch.ProcessedRecords / batch.TotalRecords * 100 
                    : 0;

                var notification = new FileProcessingProgressNotification(
                    batch.Id,
                    batch.RaceId,
                    batch.TotalRecords,
                    batch.ProcessedRecords,
                    batch.MatchedRecords,
                    batch.DuplicateRecords,
                    batch.ErrorRecords,
                    progressPercent,
                    batch.ProcessingStatus.ToString()
                );

                var groupName = SignalRGroupNames.GetRaceGroupName(batch.RaceId);
                await _hubContext.Clients.Group(groupName).FileProcessingProgress(notification);

                _logger.LogDebug("Sent FileProcessingProgress notification for batch {BatchId}: {Progress}%", 
                    batch.Id, progressPercent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send FileProcessingProgress notification for batch {BatchId}", batch.Id);
            }
        }

        public async Task NotifyFileProcessingCompleteAsync(DataEntities.FileUploadBatch batch)
        {
            try
            {
                TimeSpan? duration = null;
                if (batch.ProcessingStartedAt.HasValue && batch.ProcessingCompletedAt.HasValue)
                {
                    duration = batch.ProcessingCompletedAt.Value - batch.ProcessingStartedAt.Value;
                }

                var notification = new FileProcessingCompleteNotification(
                    batch.Id,
                    batch.RaceId,
                    batch.OriginalFileName,
                    batch.TotalRecords,
                    batch.ProcessedRecords,
                    batch.MatchedRecords,
                    batch.DuplicateRecords,
                    batch.ErrorRecords,
                    batch.ProcessingStatus.ToString(),
                    batch.ErrorMessage,
                    duration
                );

                var groupName = SignalRGroupNames.GetRaceGroupName(batch.RaceId);
                await _hubContext.Clients.Group(groupName).FileProcessingComplete(notification);

                _logger.LogInformation("Sent FileProcessingComplete notification for batch {BatchId} to race {RaceId}", 
                    batch.Id, batch.RaceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send FileProcessingComplete notification for batch {BatchId}", batch.Id);
            }
        }

        public async Task NotifyReaderHealthUpdateAsync(DataEntities.ReaderHealthStatus healthStatus, string readerName)
        {
            try
            {
                var notification = new ReaderHealthUpdateNotification(
                    healthStatus.ReaderDeviceId,
                    readerName,
                    healthStatus.IsOnline,
                    healthStatus.LastHeartbeat,
                    healthStatus.CpuTemperatureCelsius,
                    healthStatus.ReaderMode.ToString(),
                    healthStatus.TotalReadsToday
                );

                await _hubContext.Clients.Group(SignalRGroupNames.ReaderHealth).ReaderHealthUpdate(notification);

                _logger.LogDebug("Sent ReaderHealthUpdate notification for reader {ReaderId}", 
                    healthStatus.ReaderDeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send ReaderHealthUpdate notification for reader {ReaderId}", 
                    healthStatus.ReaderDeviceId);
            }
        }

        public async Task NotifyReaderAlertAsync(DataEntities.ReaderAlert alert, string readerName)
        {
            try
            {
                var notification = new ReaderAlertNotification(
                    alert.Id,
                    alert.ReaderDeviceId,
                    readerName,
                    alert.AlertType.ToString(),
                    alert.Severity.ToString(),
                    alert.Message,
                    alert.AuditProperties.CreatedDate
                );

                await _hubContext.Clients.Group(SignalRGroupNames.ReaderHealth).ReaderAlert(notification);

                _logger.LogInformation("Sent ReaderAlert notification for reader {ReaderId}: {AlertType}", 
                    alert.ReaderDeviceId, alert.AlertType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send ReaderAlert notification for alert {AlertId}", alert.Id);
            }
        }

        public async Task NotifyNewReadAsync(int raceId, string epc, int? readerDeviceId, int? checkpointId,
            string? checkpointName, DateTime readTimestamp, int? participantId,
            string? participantName, string? bibNumber)
        {
            try
            {
                var notification = new NewReadNotification(
                    epc,
                    readerDeviceId,
                    checkpointId,
                    checkpointName,
                    readTimestamp,
                    participantId,
                    participantName,
                    bibNumber
                );

                var groupName = SignalRGroupNames.GetRaceGroupName(raceId);
                await _hubContext.Clients.Group(groupName).NewRead(notification);

                _logger.LogDebug("Sent NewRead notification for EPC {Epc} to race {RaceId}", epc, raceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send NewRead notification for EPC {Epc}", epc);
            }
        }

        public async Task NotifyResultUpdateAsync(DataEntities.Results result, string participantName, string bibNumber, string raceName)
        {
            try
            {
                var notification = new ResultUpdateNotification(
                    result.Id,
                    result.ParticipantId,
                    participantName,
                    bibNumber,
                    result.RaceId,
                    raceName,
                    result.NetTime,
                    result.GunTime,
                    result.OverallRank,
                    result.GenderRank,
                    result.CategoryRank
                );

                var groupName = SignalRGroupNames.GetRaceGroupName(result.RaceId);
                await _hubContext.Clients.Group(groupName).ResultUpdate(notification);

                _logger.LogDebug("Sent ResultUpdate notification for result {ResultId} to race {RaceId}", 
                    result.Id, result.RaceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send ResultUpdate notification for result {ResultId}", result.Id);
            }
        }
    }
}
