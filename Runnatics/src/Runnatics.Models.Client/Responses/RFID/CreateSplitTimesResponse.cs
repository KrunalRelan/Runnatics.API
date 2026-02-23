namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response model for split times creation operation
    /// </summary>
    public class CreateSplitTimesResponse
    {
        public string Status { get; set; } = string.Empty;
        public int SplitTimesCreated { get; set; }
        public int ParticipantsProcessed { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
