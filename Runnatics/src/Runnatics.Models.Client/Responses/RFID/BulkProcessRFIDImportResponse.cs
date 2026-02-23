namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response for bulk processing all pending RFID batches for an event/race
    /// </summary>
    public class BulkProcessRFIDImportResponse
    {
        public DateTime ProcessedAt { get; set; }
        public string Status { get; set; } = "Processing";
        public string? Message { get; set; }
        
        public int TotalBatches { get; set; }
        public int SuccessfulBatches { get; set; }
        public int FailedBatches { get; set; }
        public int TotalProcessedReadings { get; set; }
        
        public List<BatchProcessResult> BatchResults { get; set; } = new List<BatchProcessResult>();
    }

    /// <summary>
    /// Result details for each batch processed
    /// </summary>
    public class BatchProcessResult
    {
        public string BatchId { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? DeviceId { get; set; }
        public string Status { get; set; } = "Processing";
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int UnlinkedCount { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
