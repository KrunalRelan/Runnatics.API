namespace Runnatics.Models.Client.Responses.Results
{
    public class LeaderboardDisplaySettings
    {
        public bool ShowOverallResults { get; set; } = true;
        public bool ShowCategoryResults { get; set; }
        public bool ShowGenderResults { get; set; }
        public bool ShowAgeGroupResults { get; set; }
        public bool ShowSplitTimes { get; set; }
        public bool ShowPace { get; set; }
        public bool ShowDnf { get; set; }
        public bool ShowMedalIcon { get; set; }
        public bool RankOnNet { get; set; }
        public string SortTimeField { get; set; } = "GunTime";
        public int? MaxResultsOverall { get; set; }
        public int? MaxResultsCategory { get; set; }
        public int? MaxDisplayedRecords { get; set; }
    }
}
