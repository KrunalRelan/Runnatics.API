namespace Runnatics.Models.Client.Requests.Public
{
    /// <summary>
    /// Request for the public grouped leaderboard.
    /// Not paginated — returns a full grouped result set.
    /// </summary>
    public class GetPublicLeaderboardRequest
    {
        /// <summary>Free-text search across participant names (optional).</summary>
        public string? Search { get; set; }

        /// <summary>Filter by gender (optional).</summary>
        public string? Gender { get; set; }

        /// <summary>Filter by age category (optional).</summary>
        public string? Category { get; set; }

        /// <summary>When true, returns all finishers; otherwise returns a top-N subset.</summary>
        public bool ShowAll { get; set; }
    }
}
