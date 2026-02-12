using Runnatics.Models.Client.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Results
{
    public class GetLeaderboardRequest : SearchCriteriaBase
    {
        public string EventId { get; set; } = string.Empty;

        public string RaceId { get; set; } = string.Empty;

        public string RankBy { get; set; } = "overall";

        public string? Gender { get; set; }

        public string? Category { get; set; }

        public bool IncludeSplits { get; set; } = false;
    }
}
