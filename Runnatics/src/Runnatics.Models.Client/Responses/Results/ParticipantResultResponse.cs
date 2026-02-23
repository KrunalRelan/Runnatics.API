namespace Runnatics.Models.Client.Responses.Results
{
    public class ParticipantResultResponse
    {
        public ParticipantInfo Participant { get; set; } = new();
        public ResultInfo Result { get; set; } = new();
        public List<SplitTimeInfo> Splits { get; set; } = new();
    }

    public class ParticipantInfo
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int? Age { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
    }

    public class ResultInfo
    {
        public string ResultId { get; set; } = string.Empty;
        
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
        public string? DisqualificationReason { get; set; }
        public bool IsOfficial { get; set; }
        public bool CertificateGenerated { get; set; }
    }
}
