namespace Runnatics.Models.Client.Responses.RFID
{
    public class ManualTimeResponse
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int CheckpointId { get; set; }
        public string? CheckpointName { get; set; }
        public long ChipTimeMs { get; set; }
        public long CumulativeTimeMs { get; set; }
        public long SplitTimeMs { get; set; }
        public decimal? Pace { get; set; }
        public decimal? Speed { get; set; }
        public bool IsManual { get; set; } = true;
        // Populated only when the edited checkpoint is the race finish
        public long? FinishTimeMs { get; set; }
        public string? FinishTime { get; set; }
        public int? OverallRank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
        public int? TotalFinishers { get; set; }
        public string? Status { get; set; }
    }
}
