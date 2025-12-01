namespace Runnatics.Models.Client.Requests.Participant
{
    public class ProcessImportRequest
    {
        public string ImportBatchId { get; set; }
        public string EventId { get; set; }
        public string? RaceId { get; set; }
    }
}