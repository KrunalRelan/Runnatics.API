namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response model for reprocessing specific participants
    /// </summary>
    public class ReprocessParticipantsResponse
    {
        public string Status { get; set; } = "Pending";
        public string Message { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        
        // Input
        public int TotalParticipantsRequested { get; set; }
        
        // Clearing phase
        public int ParticipantsCleared { get; set; }
        public int ReadingsCleared { get; set; }
        public int ResultsCleared { get; set; }
        
        // Reprocessing phase
        public int ParticipantsReprocessed { get; set; }
        public int ReadingsCreated { get; set; }
        public int ResultsCreated { get; set; }
        
        // Errors
        public List<string> NotFoundParticipants { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        
        public long ProcessingTimeMs { get; set; }
    }
}
