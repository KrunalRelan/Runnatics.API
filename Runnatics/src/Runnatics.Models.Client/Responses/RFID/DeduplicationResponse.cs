namespace Runnatics.Models.Client.Responses.RFID
{
    public class DeduplicationResponse
    {
        public int TotalRawReadings { get; set; }
        public int NormalizedReadings { get; set; }
        public int DuplicatesRemoved { get; set; }
        public int CheckpointsProcessed { get; set; }
        public int ParticipantsProcessed { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string Status { get; set; } = "Completed";

        /// <summary>Non-fatal note (e.g. an unusually-early but within-window earliest reading) surfaced for observability.</summary>
        public string? Message { get; set; }
    }
}
