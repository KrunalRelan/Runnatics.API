using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.RFID
{
    public class RFIDImportRequest
    {
        [Required(ErrorMessage = "File is required")]
        public required IFormFile File { get; set; }

        /// <summary>
        /// Device ID from the RFID reader (optional, will be read from file if SQLite)
        /// </summary>
        public string? DeviceId { get; set; }

        /// <summary>
        /// Expected checkpoint ID for automatic assignment (optional)
        /// </summary>
        public string? ExpectedCheckpointId { get; set; }

        /// <summary>
        /// Reader device ID if known (optional)
        /// </summary>
        public string? ReaderDeviceId { get; set; }

        /// <summary>
        /// Time zone for the readings (default: UTC)
        /// </summary>
        [MaxLength(50)]
        public string TimeZoneId { get; set; } = "UTC";

        /// <summary>
        /// File format: DB (SQLite), CSV, JSON
        /// </summary>
        [MaxLength(20)]
        public string FileFormat { get; set; } = "DB";

        /// <summary>
        /// Source type: file_upload or live_sync
        /// </summary>
        [MaxLength(20)]
        public string SourceType { get; set; } = "file_upload";
    }
}
