namespace Runnatics.Models.Client.Responses.Results
{
    public class SplitTimeCalculationResponse
    {
        public int TotalParticipants { get; set; }
        public int ParticipantsWithSplits { get; set; }
        public int TotalSplitTimesCreated { get; set; }
        public int CheckpointsProcessed { get; set; }
        public List<CheckpointSummary> CheckpointSummaries { get; set; } = new();
        public long ProcessingTimeMs { get; set; }
        public string Status { get; set; } = "Completed";
    }

    public class CheckpointSummary
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string CheckpointName { get; set; } = string.Empty;
        public decimal DistanceKm { get; set; }
        public int ParticipantCount { get; set; }
        public long? FastestTimeMs { get; set; }
        public long? SlowestTimeMs { get; set; }
        public string? FastestTimeFormatted { get; set; }
        public string? SlowestTimeFormatted { get; set; }
    }
}
