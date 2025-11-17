using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses.Events;

namespace Runnatics.Services.Interface
{
    public interface IEventsService : ISimpleServiceBase
    {
        Task<PagingList<EventResponse>> Search(EventSearchRequest request);
        Task<bool> Create(EventRequest request);
        Task<bool> Update(int id, EventRequest request);
        Task<bool> Delete(int id);
        Task<EventResponse?> GetEventById(int id);
    }
}
