namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response model for checkpoint assignment operation (loop races)
    /// </summary>
    public class AssignCheckpointsResponse
    {
        public string Status { get; set; } = string.Empty;
        public int CheckpointsAssigned { get; set; }
        public int ReadingsProcessed { get; set; }
        public int FlaggedForReview { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
