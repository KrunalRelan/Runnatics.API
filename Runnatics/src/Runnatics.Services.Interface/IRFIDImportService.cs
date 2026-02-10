using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;

namespace Runnatics.Services.Interface
{
    public interface IRFIDImportService : ISimpleServiceBase
    {
        Task<EPCMappingImportResponse> UploadEPCMappingAsync(string eventId, string raceId, EPCMappingImportRequest request);
        Task<RFIDImportResponse> UploadRFIDFileAsync(string eventId, string raceId, RFIDImportRequest request);

        Task<RFIDImportResponse> UploadRFIDFileAutoAsync(RFIDImportRequest request);

        Task<ProcessRFIDImportResponse> ProcessRFIDStagingDataAsync(ProcessRFIDImportRequest request);

        //Task<BulkProcessRFIDImportResponse> ProcessAllRFIDDataAsync(string eventId, string raceId);

        Task<DeduplicationResponse> DeduplicateAndNormalizeAsync(string eventId, string raceId);

        Task<AssignCheckpointsResponse> AssignCheckpointsForLoopRaceAsync(string eventId, string raceId);

        Task<CreateSplitTimesResponse> CreateSplitTimesFromNormalizedReadingsAsync(string eventId, string raceId);

        Task<CalculateResultsResponse> CalculateRaceResultsAsync(string eventId, string raceId);

        Task<CompleteRFIDProcessingResponse> ProcessCompleteWorkflowAsync(string eventId, string raceId);

        Task<ClearDataResponse> ClearProcessedDataAsync(string eventId, string raceId, bool keepUploads = true);

        Task<ReprocessParticipantsResponse> ReprocessParticipantsAsync(string eventId, string raceId, string[] participantIds);

        Task<ProcessRFIDImportResponse> ReprocessBatchAsync(string eventId, string raceId, string uploadBatchId);
    }
}
