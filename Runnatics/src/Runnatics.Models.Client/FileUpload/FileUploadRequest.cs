using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// Request model for uploading a file
    /// </summary>
    public class FileUploadRequest
    {
        /// <summary>
        /// The race to associate the upload with (encrypted)
        /// </summary>
        public string RaceId { get; set; } = string.Empty;

        /// <summary>
        /// Optional event ID (encrypted)
        /// </summary>
        public string? EventId { get; set; }

        /// <summary>
        /// Optional checkpoint to associate reads with (encrypted)
        /// </summary>
        public string? CheckpointId { get; set; }

        /// <summary>
        /// Optional reader device to associate reads with (encrypted)
        /// </summary>
        public string? ReaderDeviceId { get; set; }

        /// <summary>
        /// Description of the upload
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional file format override (auto-detected if not specified)
        /// </summary>
        public FileFormat? FileFormat { get; set; }

        /// <summary>
        /// Optional field mapping ID (encrypted)
        /// </summary>
        public string? MappingId { get; set; }
    }
}
