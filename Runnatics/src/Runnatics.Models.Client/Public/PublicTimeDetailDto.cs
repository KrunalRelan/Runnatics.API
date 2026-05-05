namespace Runnatics.Models.Client.Public
{
    public class PublicTimeDetailDto
    {
        public string Time { get; set; } = string.Empty;
        public string? AveragePace { get; set; }
        public int? OverallRank { get; set; }
        public int TotalOverall { get; set; }
        public int? GenderRank { get; set; }
        public int TotalGender { get; set; }
        public int? CategoryRank { get; set; }
        public int TotalCategory { get; set; }
    }
}
