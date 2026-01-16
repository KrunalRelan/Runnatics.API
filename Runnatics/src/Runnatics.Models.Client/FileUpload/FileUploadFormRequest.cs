using Microsoft.AspNetCore.Http;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// Form request model for uploading a file with all parameters
    /// </summary>
    public class FileUploadFormRequest
    {
        /// <summary>
        /// The file to upload
        /// </summary>
        public IFormFile File { get; set; } = null!;

        /// <summary>
        /// The race to associate the upload with (encrypted)
        /// </summary>
        public string RaceId { get; set; } = string.Empty;

        /// <summary>
        /// Optional event ID (encrypted)
        /// </summary>
        public string? EventId { get; set; }

        /// <summary>
        /// Optional reader device ID (encrypted)
        /// </summary>
        public string? ReaderDeviceId { get; set; }

        /// <summary>
        /// Optional checkpoint to associate reads with (encrypted)
        /// </summary>
        public string? CheckpointId { get; set; }

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

        /// <summary>
        /// Converts this form request to a FileUploadRequest for service layer processing
        /// </summary>
        public FileUploadRequest ToServiceRequest()
        {
            return new FileUploadRequest
            {
                RaceId = RaceId,
                EventId = EventId,
                ReaderDeviceId = ReaderDeviceId,
                CheckpointId = CheckpointId,
                Description = Description,
                FileFormat = FileFormat,
                MappingId = MappingId
            };
        }
    }
}
