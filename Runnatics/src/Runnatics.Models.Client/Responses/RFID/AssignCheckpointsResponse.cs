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

        // Diagnostics — filled by AssignCheckpointsForLoopRaceAsync to pinpoint pipeline failures
        public int DiagEpcMappings { get; set; }
        public int DiagReadingsAfterTimeFilter { get; set; }
        public int DiagReadingsAfterEpcFilter { get; set; }
        public int DiagReadingsAfterDeviceResolution { get; set; }
        public string? DiagRaceStartTimeStored { get; set; }
        public string? DiagRaceStartTimeUtc { get; set; }
    }
}
