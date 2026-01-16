using Microsoft.AspNetCore.Http;

namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// Form request model for uploading multiple files
    /// </summary>
    public class MultiFileUploadFormRequest
    {
        /// <summary>
        /// The files to upload
        /// </summary>
        public List<IFormFile> Files { get; set; } = [];

        /// <summary>
        /// The race to associate the uploads with (encrypted)
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
                Description = Description
            };
        }
    }
}
