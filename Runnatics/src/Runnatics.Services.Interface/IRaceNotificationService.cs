using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Service for sending real-time notifications via SignalR
    /// </summary>
    public interface IRaceNotificationService
    {
        /// <summary>
        /// Notifies clients that a file has been uploaded
        /// </summary>
        Task NotifyFileUploadedAsync(FileUploadBatch batch, string uploadedByUserName);

        /// <summary>
        /// Sends file processing progress update to clients
        /// </summary>
        Task NotifyFileProcessingProgressAsync(FileUploadBatch batch);

        /// <summary>
        /// Notifies clients that file processing is complete
        /// </summary>
        Task NotifyFileProcessingCompleteAsync(FileUploadBatch batch);

        /// <summary>
        /// Notifies clients of reader health status changes
        /// </summary>
        Task NotifyReaderHealthUpdateAsync(ReaderHealthStatus healthStatus, string readerName);

        /// <summary>
        /// Notifies clients of a new reader alert
        /// </summary>
        Task NotifyReaderAlertAsync(ReaderAlert alert, string readerName);

        /// <summary>
        /// Notifies clients of a new RFID read
        /// </summary>
        Task NotifyNewReadAsync(int raceId, string epc, int? readerDeviceId, int? checkpointId, 
            string? checkpointName, DateTime readTimestamp, int? participantId, 
            string? participantName, string? bibNumber);

        /// <summary>
        /// Notifies clients of a result update
        /// </summary>
        Task NotifyResultUpdateAsync(Results result, string participantName, string bibNumber, string raceName);
    }
}
