using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;

namespace Runnatics.Services.Interface
{
    public interface IRacesService : ISimpleServiceBase
    {
        Task<PagingList<RaceResponse>> Search(string eventId, RaceSearchRequest request);
        Task<bool> Create(string eventId, RaceRequest request);

        Task<bool> Update(string eventId, string raceId, RaceRequest request);

        Task<RaceResponse?> GetRaceById(string eventId, string raceId);

        Task<bool> Delete(string eventId, string raceId);

    }
}
