namespace Runnatics.Models.Client.Responses.Participants
{
    public class ParticipantImportResponse
    {
        public string ImportBatchId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int TotalRecords { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<ValidationError> Errors { get; set; } = [];
        public DateTime UploadedAt { get; set; }
    }
}