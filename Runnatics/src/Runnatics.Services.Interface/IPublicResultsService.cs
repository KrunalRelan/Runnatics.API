using Runnatics.Models.Client.Public;
using Runnatics.Models.Data.Entities;
using DataResultsPagingList = Runnatics.Models.Data.Common.PagingList<Runnatics.Models.Data.Entities.Results>;

namespace Runnatics.Services.Interface
{
    public interface IPublicResultsService : ISimpleServiceBase
    {
        /// <summary>
        /// Returns paged, filterable results for a public event page. No tenant filter.
        /// </summary>
        Task<DataResultsPagingList> GetPublicResultsAsync(
            int eventId,
            string? raceName,
            string? searchQuery,
            string? gender,
            int page,
            int pageSize);

        /// <summary>
        /// Returns effective leaderboard display settings for a race on the public site.
        /// Race-level settings are returned when OverrideSettings=true, otherwise event-level.
        /// </summary>
        Task<PublicLeaderboardSettingsDto> GetEffectivePublicLeaderboardSettingsAsync(
            int eventId,
            int? raceId);

        /// <summary>
        /// Returns finishers grouped by gender then age category for the public leaderboard page.
        /// </summary>
        Task<PublicGroupedLeaderboardDto?> GetPublicGroupedLeaderboardAsync(
            string eventId,
            string raceId,
            string? search,
            string? gender,
            string? category,
            bool showAll,
            CancellationToken ct = default);

        /// <summary>
        /// Returns full result details for a single participant by encrypted participant ID.
        /// </summary>
        Task<PublicParticipantDetailDto?> GetPublicParticipantDetailAsync(
            string participantId,
            CancellationToken ct = default);
    }
}
