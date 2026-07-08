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

                // #4 (a85ef01) FOLLOW-THROUGH (2026-07-07): the grid/search path shows the
                // COMPUTED result status in DISPLAY form (OK/DNF/DNS/DSQ), but this DETAILS
                // response kept the implicit AutoMapper map of the RAW stored
                // Participant.Status — the detail page's Run Status DDL rendered
                // "Registered (computed)" for a runner who ran, and its "Remove
                // disqualification" option (status === "DSQ") could never appear. Same rule
                // as the grid: with a Results row, status = ToDisplay(Result.Status); with
                // none, the raw participant status stays (honest for unprocessed runners).
                if (!string.IsNullOrEmpty(participant.Result.Status))
                    response.Status = Models.Data.Constants.ResultStatus.ToDisplay(participant.Result.Status);
            }

            // Build split times and calculate performance metrics.
            // LateStartCutOff feeds the NET split baseline (SplitBaseline) — requires the caller
            // to have included participant.Race.RaceSettings (null-safe: defaults via StartWindow).
            if (splitTimes.Any())
            {
                var performanceBuilder = new PerformanceMetricsBuilder(_mapper);
                performanceBuilder.BuildSplitTimesAndPerformance(
                    response, splitTimes, participant.Race?.RaceSettings?.LateStartCutOff);
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
