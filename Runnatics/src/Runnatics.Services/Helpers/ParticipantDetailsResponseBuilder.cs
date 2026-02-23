using AutoMapper;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Helper class for building participant details response
    /// Follows Single Responsibility Principle - only handles response building
    /// </summary>
    public class ParticipantDetailsResponseBuilder
    {
        private readonly IMapper _mapper;

        public ParticipantDetailsResponseBuilder(IMapper mapper)
        {
            _mapper = mapper;
        }

        public ParticipantDetailsResponse BuildResponse(
            Models.Data.Entities.Participant participant,
            List<SplitTimes> splitTimes,
            int totalParticipantsInRace,
            int totalInGender,
            int totalInCategory)
        {
            var response = _mapper.Map<ParticipantDetailsResponse>(participant);

            // Set timing info from result
            if (participant.Result != null)
            {
                SetTimingInfo(response, participant);
                response.Rankings = BuildRankingInfo(participant.Result, 
                    totalParticipantsInRace, totalInGender, totalInCategory);
            }

            // Build split times and calculate performance metrics
            if (splitTimes.Any())
            {
                var performanceBuilder = new PerformanceMetricsBuilder(_mapper);
                performanceBuilder.BuildSplitTimesAndPerformance(response, splitTimes);
            }

            return response;
        }

        private static void SetTimingInfo(ParticipantDetailsResponse response, Models.Data.Entities.Participant participant)
        {
            response.ChipTime = TimeFormatter.FormatTimeSpan(participant.Result!.NetTime);
            response.GunTime = TimeFormatter.FormatTimeSpan(participant.Result.GunTime);
            response.FinishTime = participant.Result.NetTime.HasValue
                ? participant.Race?.StartTime?.AddMilliseconds(participant.Result.NetTime.Value)
                : null;
        }

        private static RankingInfo BuildRankingInfo(
            Results result,
            int totalParticipants,
            int totalInGender,
            int totalInCategory)
        {
            return new RankingInfo
            {
                OverallRank = result.OverallRank,
                TotalParticipants = totalParticipants,
                OverallPercentage = CalculatePercentage(result.OverallRank, totalParticipants),

                GenderRank = result.GenderRank,
                TotalInGender = totalInGender,
                GenderPercentage = CalculatePercentage(result.GenderRank, totalInGender),

                CategoryRank = result.CategoryRank,
                TotalInCategory = totalInCategory,
                CategoryPercentage = CalculatePercentage(result.CategoryRank, totalInCategory),

                AllCategoriesRank = result.OverallRank,
                TotalAllCategories = totalParticipants,
                AllCategoriesPercentage = CalculatePercentage(result.OverallRank, totalParticipants)
            };
        }

        private static decimal? CalculatePercentage(int? rank, int total)
        {
            if (total > 0 && rank.HasValue)
            {
                return Math.Round((decimal)rank.Value / total * 100, 1);
            }
            return null;
        }
    }
}
