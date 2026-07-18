namespace Runnatics.Models.Client.Public
{
    public class PublicGroupedLeaderboardDto
    {
        public string EventName { get; set; } = string.Empty;
        public string RaceName { get; set; } = string.Empty;
        public DateTime? RaceDate { get; set; }
        public decimal? RaceDistance { get; set; }
        public string? ResultRules { get; set; }
        public string RankBy { get; set; } = "ChipTime";

        // BUG-24: Overall and Category sort independently (e.g. Overall=Chip, Category=Gun),
        // so each section carries its own self-describing sort label. Same no-space
        // "ChipTime"/"GunTime" format the frontend already string-matches on RankBy.
        public string OverallRankBy { get; set; } = "ChipTime";
        public string CategoryRankBy { get; set; } = "ChipTime";

        // BUG-24: honour the event/race "Show Overall" / "Show Category" toggles.
        // When false the corresponding list is returned empty and the public page hides the section.
        public bool ShowOverall { get; set; } = true;
        public bool ShowCategory { get; set; } = true;

        // True when the event's results are published — gates the "OFFICIAL RESULTS" badge.
        public bool ResultsPublished { get; set; }

        // Configured per-section "results to show" counts (LeaderboardSettings). Surfaced so the
        // public page can cap PER GENDER client-side (the server-side truncation is a total cap,
        // not per-gender). 0 = unset; the client applies its own default.
        public int NumberOfResultsToShowOverall { get; set; }
        public int NumberOfResultsToShowCategory { get; set; }

        public string? EventBannerBase64 { get; set; }
        public PublicPodiumDto Podium { get; set; } = new();
        public List<PublicGenderGroupDto> GenderCategories { get; set; } = [];
        public List<PublicLeaderboardEntryDto> OverallResults { get; set; } = [];
        public int TotalFinishers { get; set; }
        public int TotalParticipants { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalOverall { get; set; }
        public int TotalPages { get; set; }
    }
}
