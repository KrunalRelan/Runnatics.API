using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;

namespace Runnatics.Services.Interface
{
    public interface IRFIDImportService : ISimpleServiceBase
    {
        Task<EPCMappingImportResponse> UploadEPCMappingAsync(string eventId, string raceId, EPCMappingImportRequest request);
        Task<RFIDImportResponse> UploadRFIDFileAsync(string eventId, string raceId, RFIDImportRequest request);

        /// <summary>
        /// Upload RFID file with automatic event/race detection based on device name from filename.
        /// Extracts device name from filename, finds associated checkpoint, and determines event/race context.
        /// </summary>
        Task<RFIDImportResponse> UploadRFIDFileAutoAsync(RFIDImportRequest request);

        Task<ProcessRFIDImportResponse> ProcessRFIDStagingDataAsync(ProcessRFIDImportRequest request);

        /// <summary>
        /// Process ALL pending RFID batches for an event/race with a single call.
        /// Useful for bulk processing after multiple file uploads.
        /// </summary>
        Task<BulkProcessRFIDImportResponse> ProcessAllRFIDDataAsync(string eventId, string raceId);

        Task<DeduplicationResponse> DeduplicateAndNormalizeAsync(string eventId, string raceId);

        /// <summary>
        /// Calculate race results from normalized readings and insert into Results table.
        /// Calculates overall, gender, and category rankings.
        /// </summary>
        Task<CalculateResultsResponse> CalculateRaceResultsAsync(string eventId, string raceId);

        /// <summary>
        /// Complete RFID processing workflow: Process all pending batches, deduplicate readings, and calculate results.
        /// Optimized with bulk operations for best performance.
        /// </summary>
        Task<CompleteRFIDProcessingResponse> ProcessCompleteWorkflowAsync(string eventId, string raceId);
    }
}
