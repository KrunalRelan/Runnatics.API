namespace Runnatics.Models.Client.Requests.RFID
{
    public class ProcessRFIDImportRequest
    {
        public required string ImportBatchId { get; set; }
        public required string EventId { get; set; }
        public required string RaceId { get; set; }
    }
}
