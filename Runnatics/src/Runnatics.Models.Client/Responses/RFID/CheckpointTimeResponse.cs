namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Checkpoint crossing time for a participant
    /// </summary>
    public class CheckpointTimeResponse
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string CheckpointName { get; set; } = string.Empty;
        public DateTime? CrossingTime { get; set; }
        public string? TimeFromStart { get; set; }
        public long? TimeFromStartSeconds { get; set; }
        public string? SplitTime { get; set; }
        public long? SplitTimeSeconds { get; set; }
        public decimal DistanceFromStart { get; set; }
        public int? Rank { get; set; }
        public bool Passed { get; set; }
    }
}
