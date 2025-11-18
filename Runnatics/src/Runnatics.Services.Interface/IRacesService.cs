using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;

namespace Runnatics.Services.Interface
{
    public interface IRacesService : ISimpleServiceBase
    {
        Task<PagingList<RaceResponse>> Search(int eventId, RaceSearchRequest request);
        Task<bool> Create(int eventId, RaceRequest request);

        Task<bool> Update(int eventId, int raceId, RaceRequest request);

        Task<RaceResponse?> GetRaceById(int eventId, int raceId);

        Task<bool> Delete(int eventId, int raceId);

    }
}
