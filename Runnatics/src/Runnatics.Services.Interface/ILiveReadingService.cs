using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;

namespace Runnatics.Services.Interface
{
    public interface ILiveReadingService : ISimpleServiceBase
    {
        Task<LiveReadingResponse?> IngestAsync(
            string eventId,
            string raceId,
            LiveReadingsRequest request,
            CancellationToken ct);
    }
}
