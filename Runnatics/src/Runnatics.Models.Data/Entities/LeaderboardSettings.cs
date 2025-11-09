using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class LeaderboardSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        public bool ShowOverallResults { get; set; } = true;

        public bool ShowCategoryResults { get; set; } = true;

        public bool ShowGenderResults { get; set; } = true;

        public bool ShowAgeGroupResults { get; set; } = true;

        public bool SortByOverallChipTime { get; set; } = false;

        public bool SortByOverallGunTime { get; set; } = false;

        public bool SortByCategoryChipTime { get; set; } = false;

        public bool SortByCategoryGunTime { get; set; } = false;

        public int? NumberOfResultsToShow { get; set; }

        public bool EnableLiveLeaderboard { get; set; } = true;

        public bool ShowSplitTimes { get; set; } = true;

        public bool ShowPace { get; set; } = true;

        public bool ShowTeamResults { get; set; } = false;

        public bool ShowMedalIcon { get; set; } = true;

        public bool AllowAnonymousView { get; set; } = true;

        public int? AutoRefreshIntervalSec { get; set; }

        public int? MaxDisplayedRecords { get; set; }

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;

        // Audit Properties
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
