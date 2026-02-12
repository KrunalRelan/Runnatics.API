namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response model for clearing processed RFID data
    /// </summary>
    public class ClearDataResponse
    {
        public string Status { get; set; } = "Pending";
        public string Message { get; set; } = string.Empty;
        public DateTime ClearedAt { get; set; } = DateTime.UtcNow;
        
        // Statistics
        public int ResultsCleared { get; set; }
        public int NormalizedReadingsCleared { get; set; }
        public int AssignmentsCleared { get; set; }
        public int ReadingsReset { get; set; }
        public int BatchesReset { get; set; }
        public int UploadsDeleted { get; set; }
        
        // Summary
        public string Summary => 
            $"Cleared {ResultsCleared} results, {NormalizedReadingsCleared} normalized readings, " +
            $"{AssignmentsCleared} checkpoint assignments. Reset {BatchesReset} batches.";
    }
}
