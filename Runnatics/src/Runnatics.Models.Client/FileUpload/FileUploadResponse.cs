using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// Response model for file upload
    /// </summary>
    public class FileUploadResponse
    {
        public int BatchId { get; set; }
        public Guid BatchGuid { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public FileFormat FileFormat { get; set; }
        public FileProcessingStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
