namespace Runnatics.Models.Client.Responses.Results
{
    public class LeaderboardResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public string RankBy { get; set; } = "overall";
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? EventName { get; set; }
        public string? RaceName { get; set; }
        public List<LeaderboardEntry> Results { get; set; } = new();
        public LeaderboardDisplaySettings DisplaySettings { get; set; } = new();
    }
}
