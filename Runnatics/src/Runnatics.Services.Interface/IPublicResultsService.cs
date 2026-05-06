using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Public;

namespace Runnatics.Services.Interface
{
    public interface IPublicResultsService : ISimpleServiceBase
    {
        /// <summary>
        /// Returns the full results response for a public event page, including publish-gate checks,
        /// leaderboard settings, and DNF filtering. Returns null when the event is not found.
        /// </summary>
        Task<PublicResultsResponseDto?> GetPublicEventResultsAsync(
            string slug,
            GetPublicEventResultsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the result DTO for a single participant identified by bib number within a public event.
        /// Returns null when the event or bib is not found.
        /// </summary>
        Task<PublicResultDto?> GetPublicResultByBibAsync(
            string slug,
            string bib,
            CancellationToken ct = default);

        /// <summary>
        /// Returns finishers grouped by gender then age category for the public leaderboard page.
        /// </summary>
        Task<PublicGroupedLeaderboardDto?> GetPublicGroupedLeaderboardAsync(
            string eventId,
            string raceId,
            GetPublicLeaderboardRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Returns full result details for a single participant by encrypted participant ID.
        /// </summary>
        Task<PublicParticipantDetailDto?> GetPublicParticipantDetailAsync(
            string participantId,
            CancellationToken ct = default);
    }
}
