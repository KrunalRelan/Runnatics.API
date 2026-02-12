namespace Runnatics.Models.Client.Requests.Participant
{
    public class ProcessImportRequest
    {
        public string ImportBatchId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string? RaceId { get; set; }
    }
}