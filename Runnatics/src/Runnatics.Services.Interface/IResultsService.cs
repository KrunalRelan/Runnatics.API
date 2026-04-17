using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
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
    }
}
