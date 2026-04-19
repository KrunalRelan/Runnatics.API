using Runnatics.Models.Client.Responses.RFID;

namespace Runnatics.Services.Interface
{
    public interface IRFIDDiagnosticsService : ISimpleServiceBase
    {
        Task<RFIDDiagnosticsResponse> DiagnoseProcessingAsync(string eventId, string raceId);
    }
}
