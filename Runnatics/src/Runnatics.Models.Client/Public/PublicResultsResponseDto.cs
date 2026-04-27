namespace Runnatics.Models.Client.Public
{
    /// <summary>
    /// Public results response shape.
    /// Field names are deliberately matched to the frontend TypeScript interface
    /// (camelCase serialization: Results→results, Races→races, etc.).
    /// </summary>
    public class PublicResultsResponseDto
    {
        /// <summary>Paged result rows. Serialises as "results" in JSON.</summary>
        public List<PublicResultDto> Results { get; set; } = [];

        /// <summary>Distinct race/category names for the filter dropdown.</summary>
        public List<string> Races { get; set; } = [];

        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasNext => Page < TotalPages;
        public bool HasPrevious => Page > 1;

        /// <summary>Effective leaderboard display settings for the selected race.</summary>
        public PublicLeaderboardSettingsDto LeaderboardSettings { get; set; } = new();

        /// <summary>
        /// False when the event's results have not been published yet.
        /// Frontend shows an informational message and an empty list.
        /// </summary>
        public bool IsPublished { get; set; } = true;

        /// <summary>Human-readable status message when IsPublished=false or results are unavailable.</summary>
        public string? StatusMessage { get; set; }
    }
}
