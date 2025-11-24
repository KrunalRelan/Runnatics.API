namespace Runnatics.Models.Client.Requests.Participant
{
    public class ProcessImportRequest
    {
        public int ImportBatchId { get; set; }
        public int EventId { get; set; }
        public int? RaceId { get; set; }
    }
}