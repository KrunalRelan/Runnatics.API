using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Models.Client.Responses.RFID;

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
        /// Records a manual finish time for a participant, then recalculates rankings for
        /// the entire race so overall/gender/category positions reflect the new entry.
        /// </summary>
        Task<ManualTimeResponse?> RecordManualTimeAsync(
            string eventId,
            string raceId,
            string participantId,
            long finishTimeMs,
            string checkpointId);
    }
}
