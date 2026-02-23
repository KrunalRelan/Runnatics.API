using Runnatics.Models.Client.Responses.Participants;

namespace Runnatics.Models.Client.Responses.RFID
{
    public class ProcessRFIDImportResponse
    {
        public int ImportBatchId { get; set; }
        public DateTime ProcessedAt { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int UnlinkedCount { get; set; }
        public string Status { get; set; } = "Processing";
        public List<string> UnlinkedEPCs { get; set; } = new List<string>();
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();
    }
}
