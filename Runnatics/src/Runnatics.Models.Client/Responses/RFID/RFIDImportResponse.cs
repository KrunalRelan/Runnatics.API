using Runnatics.Models.Client.Responses.Participants;

namespace Runnatics.Models.Client.Responses.RFID
{
    public class RFIDImportResponse
    {
        public string? UploadBatchId { get; set; }  // Changed from ImportBatchId
        public string FileName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public int TotalReadings { get; set; }  // Changed from TotalRecords
        public int UniqueEpcs { get; set; }  // New: unique RFID tags
        public long? TimeRangeStart { get; set; }  // New: earliest timestamp
        public long? TimeRangeEnd { get; set; }  // New: latest timestamp
        public long FileSizeBytes { get; set; }  // New: file size
        public string FileFormat { get; set; } = "DB";  // DB (SQLite), CSV, JSON
        public string Status { get; set; } = "uploading";  // uploading, uploaded, processing, completed, failed
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();
    }
}
