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

        /// <summary>
        /// When true, bypasses the ShowLeaderboard and Published visibility gates.
        /// Set by admin-only callers (e.g. Excel export) that must see results
        /// regardless of public-facing visibility settings.
        /// </summary>
        public bool SkipPublishGates { get; set; } = false;
    }
}
