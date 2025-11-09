using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Events
{
    public class LeaderboardSettingsRequest
    {
        public bool ShowOverallResults { get; set; }

        public bool ShowCategoryResults { get; set; }

        public bool ShowGenderResults { get; set; }

        public bool ShowAgeGroupResults { get; set; }

        public bool SortByOverallChipTime { get; set; }

        public bool SortByOverallGunTime { get; set; }

        public bool SortByCategoryChipTime { get; set; }

        public bool SortByCategoryGunTime { get; set; }

        public bool EnableLiveLeaderboard { get; set; }

        public bool ShowSplitTimes { get; set; }

        public bool ShowPace { get; set; }

        public bool ShowTeamResults { get; set; } = false;

        public bool ShowMedalIcon { get; set; }

        public bool AllowAnonymousView { get; set; }

        public int? AutoRefreshIntervalSec { get; set; }

        public int? MaxDisplayedRecords { get; set; }

        public int? NumberOfResultsToShowOverall { get; set; }

        public int? NumberOfResultsToShowCategory { get; set; }
    }
}
