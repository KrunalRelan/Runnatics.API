namespace Runnatics.Models.Client.Responses.Participants
{
    public class ParticipantImportResponse
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; }
        public int TotalRecords { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
        public string Status { get; set; }
        public List<ValidationError> Errors { get; set; }
        public DateTime UploadedAt { get; set; }

        public ParticipantImportResponse()
        {
            Errors = [];
        }
    }
}