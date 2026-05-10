namespace Runnatics.Models.Client.Requests.Public
{
    public class GetPublicLeaderboardRequest
    {
        public string? Search { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public bool ShowAll { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
