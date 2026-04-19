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
        /// Includes Races and EventOrganizer.
        /// The dictionary maps RaceId → active participant count (lightweight COUNT query).
        /// Returns (null, null) when not found or deleted.
        /// </summary>
        Task<(Event? Event, Dictionary<int, int>? RaceParticipantCounts)> GetPublicEventBySlugAsync(string slug);

        /// <summary>
        /// Stores a base64-encoded banner image on the event.
        /// </summary>
        Task<bool> UpdateBannerAsync(string eventId, string base64Image, string contentType);

        /// <summary>
        /// Returns the banner image as (base64, contentType). Both null if no banner.
        /// </summary>
        Task<(string? Base64, string? ContentType)> GetBannerAsync(string eventId);
    }
}
