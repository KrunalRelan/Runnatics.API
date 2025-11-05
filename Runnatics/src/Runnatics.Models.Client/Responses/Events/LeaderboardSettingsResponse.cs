namespace Runnatics.Models.Client.Responses.Events
{
    public class LeaderboardSettingsResponse
    {
        public int Id { get; set; }

        public int EventId { get; set; }

        public bool ShowOverallResults { get; set; }

        public bool ShowCategoryResults { get; set; }

        public bool ShowGenderResults { get; set; }

        public bool ShowAgeGroupResults { get; set; }

        public bool SortByOverallChipTime { get; set; }
        public bool SortByOverallGunTime { get; set; }
        public bool SortByCategoryChipTime { get; set; }
        public bool SortByCategoryGunTime { get; set; }
        public int NumberOfResultsToShow { get; set; }
        public bool EnableLiveLeaderboard { get; set; }

        public bool ShowSplitTimes { get; set; }

        public bool ShowPace { get; set; }

        public bool ShowTeamResults { get; set; }

        public bool ShowMedalIcon { get; set; }

        public bool AllowAnonymousView { get; set; }

        public int AutoRefreshIntervalSec { get; set; }

        public int MaxDisplayedRecords { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
