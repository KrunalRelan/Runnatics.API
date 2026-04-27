namespace Runnatics.Models.Client.Public
{
    /// <summary>
    /// Effective leaderboard display settings for a public results page.
    /// Derived from race-level override if OverrideSettings=true, else event-level.
    /// All booleans default to true/false so the public page renders sensibly
    /// even when no settings row exists for the event.
    /// </summary>
    public class PublicLeaderboardSettingsDto
    {
        public bool ShowOverallResults { get; set; } = true;
        public bool ShowCategoryResults { get; set; } = true;
        public bool ShowGenderResults { get; set; } = false;
        public bool ShowAgeGroupResults { get; set; } = false;
        public bool SortByOverallChipTime { get; set; } = true;
        public bool SortByOverallGunTime { get; set; } = false;
        public bool SortByCategoryChipTime { get; set; } = true;
        public bool SortByCategoryGunTime { get; set; } = false;
        public bool EnableLiveLeaderboard { get; set; } = false;
        public bool ShowSplitTimes { get; set; } = false;
        public bool ShowPace { get; set; } = false;
        public bool ShowTeamResults { get; set; } = false;
        public bool ShowMedalIcon { get; set; } = true;
        public int AutoRefreshIntervalSec { get; set; } = 30;
        public int MaxDisplayedRecords { get; set; } = 0;       // 0 = no cap
        public int NumberOfResultsToShowOverall { get; set; } = 0;    // 0 = no cap
        public int NumberOfResultsToShowCategory { get; set; } = 0;   // 0 = no cap

        // TODO: AllowAnonymousView — stored in DB but no public UI gate yet.
        // TODO: ShowTeamResults — stored in DB; team results tab not implemented on public site.
    }
}
