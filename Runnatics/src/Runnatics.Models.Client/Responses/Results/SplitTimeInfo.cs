namespace Runnatics.Models.Client.Responses.Results
{
    public class SplitTimeInfo
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string CheckpointName { get; set; } = string.Empty;
        public decimal DistanceKm { get; set; }
        public long SplitTimeMs { get; set; }
        public long? SegmentTimeMs { get; set; }
        public string SplitTime { get; set; } = string.Empty;
        public string? SegmentTime { get; set; }
        public decimal? Pace { get; set; } // min/km
        public string? PaceFormatted { get; set; }
        public int? Rank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
    }
}
