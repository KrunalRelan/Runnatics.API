using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Results;

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
        Task<LeaderboardResponse> GetLeaderboardAsync(
            string eventId, 
            string raceId, 
            string rankBy = "overall",
            string? gender = null,
            string? category = null,
            int page = 1,
            int pageSize = 50,
            bool includeSplits = false);

        /// <summary>
        /// Gets detailed results for a specific participant
        /// </summary>
        Task<ParticipantResultResponse?> GetParticipantResultAsync(
            string eventId, 
            string raceId, 
            string participantId);
    }
}
