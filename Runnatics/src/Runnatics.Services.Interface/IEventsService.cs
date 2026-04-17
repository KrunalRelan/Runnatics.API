using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Models.Data.Entities;
using DataPagingList = Runnatics.Models.Data.Common.PagingList<Runnatics.Models.Data.Entities.Event>;

namespace Runnatics.Services.Interface
{
    public interface IEventsService : ISimpleServiceBase
    {
        Task<PagingList<EventResponse>> Search(EventSearchRequest request);
        Task<bool> Create(EventRequest request);
        Task<bool> Update(string id, EventRequest request);
        Task<bool> Delete(string id);

        Task<EventResponse?> GetEventById(string id);

        Task<PagingList<EventResponse>> SearchFutureEvents(EventSearchRequest request);

        Task<PagingList<EventResponse>> SearchPastEvents(EventSearchRequest request);

        /// <summary>
        /// Returns a paged list of public events (no tenant filter).
        /// Includes the Races collection so callers can build category lists.
        /// </summary>
        Task<DataPagingList> GetPublicEventsAsync(
            bool isPast, string? city, string? searchQuery, int page, int pageSize);

        /// <summary>
        /// Returns a single public event by its URL slug.
        /// Includes Races (with Participants for count) and EventOrganizer.
        /// Returns null when not found or deleted.
        /// </summary>
        Task<Event?> GetPublicEventBySlugAsync(string slug);
    }
}
