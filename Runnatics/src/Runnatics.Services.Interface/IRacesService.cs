using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Races;
using Runnatics.Models.Client.Responses.Races;

namespace Runnatics.Services.Interface
{
    public interface IRacesService : ISimpleServiceBase
    {
        Task<PagingList<RaceResponse>> Search(RaceSearchRequest request);
        Task<bool> Create(RaceRequest request);

    }
}
