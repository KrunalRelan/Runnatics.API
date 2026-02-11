using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class UploadBatch
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// RaceId is optional for event-level uploads where a single file may contain
        /// data for multiple races. Race association is determined during processing
        /// via the EPC → Participant → RaceId chain.
        /// </summary>
        public int? RaceId { get; set; }

        [Required]
        public int EventId { get; set; }

        [Required]
        [MaxLength(50)]
        public string DeviceId { get; set; } = string.Empty;

        public int? ReaderDeviceId { get; set; }

        public int? ExpectedCheckpointId { get; set; }

        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? StoredFilePath { get; set; }

        public long FileSizeBytes { get; set; }

        [MaxLength(50)]
        public string? FileHash { get; set; }

        [MaxLength(20)]
        public string FileFormat { get; set; } = "DB"; // DB (SQLite), CSV, JSON

        [MaxLength(20)]
        public string Status { get; set; } = "uploading"; // uploading, uploaded, processing, completed, failed

        public int? TotalReadings { get; set; }

        public int? UniqueEpcs { get; set; }

        public long? TimeRangeStart { get; set; }

        public long? TimeRangeEnd { get; set; }

        [MaxLength(20)]
        public string SourceType { get; set; } = "file_upload"; // file_upload, live_sync

        public bool IsLiveSync { get; set; } = false;

        public DateTime? ProcessingStartedAt { get; set; }

        public DateTime? ProcessingCompletedAt { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Race Race { get; set; } = null!;
        public virtual Event Event { get; set; } = null!;
        public virtual Device? ReaderDevice { get; set; }
        public virtual Checkpoint? ExpectedCheckpoint { get; set; }
        public virtual ICollection<RawRFIDReading> Readings { get; set; } = new List<RawRFIDReading>();
    }
}
