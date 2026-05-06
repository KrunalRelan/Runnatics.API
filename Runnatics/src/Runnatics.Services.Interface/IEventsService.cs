using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses.Events;

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
        /// Returns a paged list of public events mapped to summary DTOs (no tenant filter).
        /// status: "upcoming" = future, "past" = past, null/"recent" = all (desc).
        /// take overrides pageSize when provided and > 0.
        /// </summary>
        Task<PublicPagedResultDto<PublicEventSummaryDto>> GetPublicEventsAsync(
            string? status, string? city, string? searchQuery, int page, int pageSize,
            int? take = null, int? year = null);

        /// <summary>
        /// Returns full public event detail by URL slug, mapped to a DTO.
        /// Returns null when not found or deleted.
        /// </summary>
        Task<PublicEventDetailDto?> GetPublicEventBySlugAsync(string slug);

        /// <summary>
        /// Returns aggregate public statistics (upcoming/past event counts).
        /// </summary>
        Task<PublicStatsDto> GetPublicStatsAsync(CancellationToken ct = default);

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
