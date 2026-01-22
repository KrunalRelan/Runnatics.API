using Runnatics.Models.Client.Responses.Participants;

namespace Runnatics.Models.Client.Responses.RFID
{
    public class RFIDImportResponse
    {
        public string? ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public int TotalRecords { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
        public string Status { get; set; } = "Pending";
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();
    }
}
