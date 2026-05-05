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
        public List<PublicGenderGroupDto> GenderCategories { get; set; } = [];
        public int TotalFinishers { get; set; }
        public int TotalParticipants { get; set; }
    }
}
