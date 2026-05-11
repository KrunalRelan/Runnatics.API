namespace Runnatics.Models.Client.Responses.Results
{
    public class SplitTimeInfo
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string CheckpointName { get; set; } = string.Empty;
        public decimal DistanceKm { get; set; }
        public long SplitTimeMs { get; set; }       // Cumulative ms from gun start (raw, for client-side use)
        public long? SegmentTimeMs { get; set; }    // Interval ms between consecutive checkpoints
        public long CumulativeTimeMs { get; set; }  // ms elapsed from start-line crossing (SplitTimeMs - start.SplitTimeMs)
        public string SplitTime { get; set; } = string.Empty;      // Formatted SegmentTimeMs (interval)
        public string? SegmentTime { get; set; }                    // Same as SplitTime (kept for compatibility)
        public string CumulativeTime { get; set; } = string.Empty; // Formatted CumulativeTimeMs
        public decimal? Pace { get; set; } // min/km
        public string? PaceFormatted { get; set; }
        public int? Rank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
    }
}
