namespace Runnatics.Models.Client.Responses.Results
{
    public class LeaderboardResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public string RankBy { get; set; } = "overall";
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public List<LeaderboardEntry> Results { get; set; } = new();
    }

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Gender { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int? Age { get; set; }
        public string City { get; set; } = string.Empty;
        
        // Times
        public long? FinishTimeMs { get; set; }
        public long? GunTimeMs { get; set; }
        public long? NetTimeMs { get; set; }
        public string? FinishTime { get; set; }
        public string? GunTime { get; set; }
        public string? NetTime { get; set; }
        
        // Rankings
        public int? OverallRank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
        
        // Performance metrics
        public decimal? AveragePace { get; set; } // min/km
        public string? AveragePaceFormatted { get; set; }
        
        public string Status { get; set; } = "Finished";
        public List<SplitTimeInfo>? Splits { get; set; }
    }

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
