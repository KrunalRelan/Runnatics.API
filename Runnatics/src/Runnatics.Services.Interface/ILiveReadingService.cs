using Runnatics.Models.Client.Requests.RFID;
using Runnatics.Models.Client.Responses.RFID;

namespace Runnatics.Services.Interface
{
    public interface ILiveReadingService : ISimpleServiceBase
    {
        /// <summary>
        /// BLIND ingest (2026-07-07) — no event/race from the caller: the device
        /// (MAC first, registered name as fallback — both from query parameters)
        /// resolves event → race → checkpoint exactly like the offline import-auto
        /// upload (shared resolver; event-level batch; race per read via
        /// EPC → Participant downstream).
        /// </summary>
        Task<LiveReadingResponse?> IngestAsync(
            string? deviceMac,
            string? deviceName,
            LiveReadingsRequest request,
            CancellationToken ct);
    }
}
