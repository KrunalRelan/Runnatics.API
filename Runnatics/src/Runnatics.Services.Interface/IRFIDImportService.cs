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

        /// <summary>
        /// Clears all processed data (ReadNormalized, Results, Assignments) for a race.
        /// Optionally preserves raw uploads for reprocessing.
        /// Use this when checkpoint mappings change or data needs to be recalculated from scratch.
        /// </summary>
        /// <param name="eventId">Encrypted event ID</param>
        /// <param name="raceId">Encrypted race ID</param>
        /// <param name="keepUploads">If true, preserves raw uploads and batches (default). If false, deletes everything.</param>
        /// <returns>Summary of cleared data</returns>
        Task<ClearDataResponse> ClearProcessedDataAsync(string eventId, string raceId, bool keepUploads = true);

        /// <summary>
        /// Clears and reprocesses data for specific participants only.
        /// Useful after manual corrections to participant data (BIB changes, chip reassignments, etc.)
        /// </summary>
        /// <param name="eventId">Encrypted event ID</param>
        /// <param name="raceId">Encrypted race ID</param>
        /// <param name="participantIds">Array of encrypted participant IDs to reprocess</param>
        /// <returns>Summary of reprocessing results</returns>
        Task<ReprocessParticipantsResponse> ReprocessParticipantsAsync(string eventId, string raceId, string[] participantIds);

        /// <summary>
        /// Reprocesses a single upload batch (e.g., after fixing device-to-checkpoint mapping).
        /// Clears processed data for readings in this batch only, then reprocesses them.
        /// </summary>
        /// <param name="eventId">Encrypted event ID</param>
        /// <param name="raceId">Encrypted race ID</param>
        /// <param name="uploadBatchId">Encrypted batch ID to reprocess</param>
        /// <returns>Processing results for the batch</returns>
        Task<ProcessRFIDImportResponse> ReprocessBatchAsync(string eventId, string raceId, string uploadBatchId);
    }
}
