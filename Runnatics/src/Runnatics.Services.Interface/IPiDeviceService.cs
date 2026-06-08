using Runnatics.Models.Client.Responses.PiDevice;

namespace Runnatics.Services.Interface
{
    public interface IPiDeviceService : ISimpleServiceBase
    {
        Task<List<PiEventDto>?> GetActiveEventsWithRacesAsync(CancellationToken ct);
    }
}
