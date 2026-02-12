namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Individual participant result with checkpoint crossing times
    /// </summary>
    public class RaceParticipantResultResponse
    {
        public int? Rank { get; set; }
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? ChipId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? TotalTime { get; set; }
        public long? TotalTimeSeconds { get; set; }
        public string? AveragePace { get; set; }
        public decimal? AverageSpeed { get; set; }
        public List<CheckpointTimeResponse> CheckpointTimes { get; set; } = new();
    }
}
