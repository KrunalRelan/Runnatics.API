namespace Runnatics.Models.Client.Public
{
    public class PublicLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string? ChipTime { get; set; }
        public string? GunTime { get; set; }
        public string? ParticipantDetailUrl { get; set; }
    }
}
