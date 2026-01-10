namespace Runnatics.Services.Interface.Hubs
{
    /// <summary>
    /// Interface for strongly-typed SignalR client methods
    /// </summary>
    public interface IRaceHubClient
    {
        /// <summary>
        /// Notifies clients when a new file has been uploaded
        /// </summary>
        Task FileUploaded(FileUploadedNotification notification);

        /// <summary>
        /// Sends file processing progress updates
        /// </summary>
        Task FileProcessingProgress(FileProcessingProgressNotification notification);

        /// <summary>
        /// Notifies clients when file processing is complete
        /// </summary>
        Task FileProcessingComplete(FileProcessingCompleteNotification notification);

        /// <summary>
        /// Notifies clients of reader health status changes
        /// </summary>
        Task ReaderHealthUpdate(ReaderHealthUpdateNotification notification);

        /// <summary>
        /// Notifies clients of new reader alerts
        /// </summary>
        Task ReaderAlert(ReaderAlertNotification notification);

        /// <summary>
        /// Notifies clients of new RFID reads in real-time
        /// </summary>
        Task NewRead(NewReadNotification notification);

        /// <summary>
        /// Notifies clients of result updates
        /// </summary>
        Task ResultUpdate(ResultUpdateNotification notification);
    }

    #region Notification DTOs

    public record FileUploadedNotification(
        int BatchId,
        Guid BatchGuid,
        string FileName,
        int RaceId,
        string UploadedBy,
        DateTime UploadedAt
    );

    public record FileProcessingProgressNotification(
        int BatchId,
        int RaceId,
        int TotalRecords,
        int ProcessedRecords,
        int MatchedRecords,
        int DuplicateRecords,
        int ErrorRecords,
        double ProgressPercent,
        string Status
    );

    public record FileProcessingCompleteNotification(
        int BatchId,
        int RaceId,
        string FileName,
        int TotalRecords,
        int ProcessedRecords,
        int MatchedRecords,
        int DuplicateRecords,
        int ErrorRecords,
        string Status,
        string? ErrorMessage,
        TimeSpan? ProcessingDuration
    );

    public record ReaderHealthUpdateNotification(
        int ReaderDeviceId,
        string ReaderName,
        bool IsOnline,
        DateTime? LastHeartbeat,
        decimal? CpuTemperature,
        string ReaderMode,
        long TotalReadsToday
    );

    public record ReaderAlertNotification(
        long AlertId,
        int ReaderDeviceId,
        string ReaderName,
        string AlertType,
        string Severity,
        string Message,
        DateTime CreatedAt
    );

    public record NewReadNotification(
        string Epc,
        int? ReaderDeviceId,
        int? CheckpointId,
        string? CheckpointName,
        DateTime ReadTimestamp,
        int? ParticipantId,
        string? ParticipantName,
        string? BibNumber
    );

    public record ResultUpdateNotification(
        int ResultId,
        int ParticipantId,
        string ParticipantName,
        string BibNumber,
        int RaceId,
        string RaceName,
        long? NetTime,
        long? GunTime,
        int? OverallRank,
        int? GenderRank,
        int? CategoryRank
    );

    #endregion
}
