using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class LeaderboardSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        public int? RaceId { get; set; }

        public bool? ShowOverallResults { get; set; }

        public bool? ShowCategoryResults { get; set; }

        public bool? ShowGenderResults { get; set; }

        public bool? ShowAgeGroupResults { get; set; }

        public bool? SortByOverallChipTime { get; set; }

        public bool? SortByOverallGunTime { get; set; }

        public bool? SortByCategoryChipTime { get; set; }

        public bool? SortByCategoryGunTime { get; set; }

        public bool? EnableLiveLeaderboard { get; set; }

        public bool? ShowSplitTimes { get; set; }

        public bool? ShowPace { get; set; }

        public bool? ShowTeamResults { get; set; }

        public bool? ShowMedalIcon { get; set; }

        public bool? AllowAnonymousView { get; set; }

        public int? AutoRefreshIntervalSec { get; set; }

        public int? MaxDisplayedRecords { get; set; }

        public int? NumberOfResultsToShowOverall { get; set; }

        public int? NumberOfResultsToShowCategory { get; set; }

        public bool? OverrideSettings { get; set; }

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;

        // Audit Properties
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
