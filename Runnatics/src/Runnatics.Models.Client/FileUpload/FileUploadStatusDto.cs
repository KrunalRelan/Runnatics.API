using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// DTO for file upload batch status
    /// </summary>
    public class FileUploadStatusDto
    {
        public int BatchId { get; set; }
        public Guid BatchGuid { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public FileFormat FileFormat { get; set; }
        public FileProcessingStatus Status { get; set; }
        public string StatusText => Status.ToString();
        public int TotalRecords { get; set; }
        public int ProcessedRecords { get; set; }
        public int MatchedRecords { get; set; }
        public int DuplicateRecords { get; set; }
        public int ErrorRecords { get; set; }
        public double ProgressPercent => TotalRecords > 0 ? (double)ProcessedRecords / TotalRecords * 100 : 0;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? ProcessingCompletedAt { get; set; }
        public string? ProcessingDuration => ProcessingStartedAt.HasValue && ProcessingCompletedAt.HasValue
            ? (ProcessingCompletedAt.Value - ProcessingStartedAt.Value).ToString(@"hh\:mm\:ss")
            : null;
        public int? UploadedByUserId { get; set; }
        public string? UploadedByUserName { get; set; }
        public int RaceId { get; set; }
        public string? RaceName { get; set; }
        public int? CheckpointId { get; set; }
        public string? CheckpointName { get; set; }
    }
}
