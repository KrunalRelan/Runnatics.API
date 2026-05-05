namespace Runnatics.Models.Client.Public
{
    public class PublicSplitDetailDto
    {
        public string Checkpoint { get; set; } = string.Empty;
        public string? SplitTime { get; set; }
        public string? RaceTime { get; set; }
        public int? RaceRank { get; set; }
        public decimal? SplitDist { get; set; }
        public string? Pace { get; set; }
        public decimal? Speed { get; set; }
    }
}
