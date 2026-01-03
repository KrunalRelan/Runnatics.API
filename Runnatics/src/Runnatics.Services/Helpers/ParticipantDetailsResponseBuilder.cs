using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Helper class for building participant details response
    /// Follows Single Responsibility Principle - only handles response building
    /// </summary>
    public class ParticipantDetailsResponseBuilder
    {
        private readonly IEncryptionService _encryptionService;

        public ParticipantDetailsResponseBuilder(IEncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        public ParticipantDetailsResponse BuildResponse(
            Models.Data.Entities.Participant participant,
            List<SplitTime> splitTimes,
            int totalParticipantsInRace,
            int totalInGender,
            int totalInCategory)
        {
            var response = new ParticipantDetailsResponse
            {
                Id = _encryptionService.Encrypt(participant.Id.ToString()),
                BibNumber = participant.BibNumber,
                FirstName = participant.FirstName,
                LastName = participant.LastName,
                Gender = participant.Gender,
                Age = participant.Age,
                AgeCategory = participant.AgeCategory,
                Club = null, // Not in current schema
                Status = participant.Status,
                Email = participant.Email,
                Phone = participant.Phone,
                Country = participant.Country,
                EventId = _encryptionService.Encrypt(participant.EventId.ToString()),
                EventName = participant.Event?.Name,
                RaceId = _encryptionService.Encrypt(participant.RaceId.ToString()),
                RaceName = participant.Race?.Title,
                RaceDistance = participant.Race?.Distance,
                StartTime = participant.Race?.StartTime
            };

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
                var performanceBuilder = new PerformanceMetricsBuilder(_encryptionService);
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
