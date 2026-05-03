using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Models.Data.Entities;
using DataResultsPagingList = Runnatics.Models.Data.Common.PagingList<Runnatics.Models.Data.Entities.Results>;

namespace Runnatics.Services.Interface
{
    public interface IResultsService : ISimpleServiceBase
    {
        /// <summary>
        /// Calculates split times for all participants at each checkpoint
        /// </summary>
        Task<SplitTimeCalculationResponse> CalculateSplitTimesAsync(CalculateSplitTimesRequest request);

        /// <summary>
        /// Calculates final results, rankings, and identifies finishers
        /// </summary>
        Task<ResultsCalculationResponse> CalculateResultsAsync(CalculateResultsRequest request);

        /// <summary>
        /// Gets leaderboard with filtering and pagination
        /// </summary>
        Task<LeaderboardResponse> GetLeaderboardAsync(GetLeaderboardRequest request);

        /// <summary>
        /// Gets detailed results for a specific participant
        /// </summary>
        Task<ParticipantResultResponse?> GetParticipantResultAsync(
            string eventId,
            string raceId,
            string participantId);

        /// <summary>
        /// Gets comprehensive participant details including performance, rankings, split times, and RFID readings
        /// </summary>
        Task<ParticipantDetailsResponse?> GetParticipantDetailsAsync(
            string eventId,
            string raceId,
            string participantId);

        /// <summary>
        /// Returns paged, filterable results for a public event page.
        /// Includes Participant, Race, and SplitTimes → ToCheckpoint nav properties.
        /// No tenant filter — all active, non-deleted results for the event.
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
        /// Returns defaults (all-true/sensible values) when no settings row exists.
        /// </summary>
        Task<PublicLeaderboardSettingsDto> GetEffectivePublicLeaderboardSettingsAsync(
            int eventId,
            int? raceId);

        /// <summary>
        /// Records a manual finish time for a participant, then recalculates rankings for
        /// the entire race so overall/gender/category positions reflect the new entry.
        /// </summary>
        Task<ManualTimeResponse?> RecordManualTimeAsync(
            string eventId,
            string raceId,
            string participantId,
            long finishTimeMs);
    }
}
