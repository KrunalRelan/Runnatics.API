namespace Runnatics.Models.Client.Responses.Participants
{
    public class ProcessImportResponse
    {
        public int ImportBatchId { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public string? Status { get; set; }
        public DateTime ProcessedAt { get; set; }
        public List<ProcessingError> Errors { get; set; }

        public ProcessImportResponse() => Errors = [];
    }
}