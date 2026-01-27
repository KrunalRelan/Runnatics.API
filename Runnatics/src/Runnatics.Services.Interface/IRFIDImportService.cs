using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;

namespace Runnatics.Services.Interface
{
    public interface IRFIDImportService : ISimpleServiceBase
    {
        Task<EPCMappingImportResponse> UploadEPCMappingAsync(string eventId, string raceId, EPCMappingImportRequest request);
        Task<RFIDImportResponse> UploadRFIDFileAsync(string eventId, string raceId, RFIDImportRequest request);
        Task<ProcessRFIDImportResponse> ProcessRFIDStagingDataAsync(ProcessRFIDImportRequest request);
        Task<DeduplicationResponse> DeduplicateAndNormalizeAsync(string eventId, string raceId);
    }
}
