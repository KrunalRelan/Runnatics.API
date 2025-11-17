using Runnatics.Models.Client.Requests.Races;

namespace Runnatics.Services.Interface
{
    public interface IRacesService : ISimpleServiceBase
    {
        Task<bool> Create(RaceRequest request);

    }
}
