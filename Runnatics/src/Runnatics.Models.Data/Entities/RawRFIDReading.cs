using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class RawRFIDReading
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int BatchId { get; set; }

        [Required]
        [MaxLength(50)]
        public string DeviceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Epc { get; set; } = string.Empty;

        [Required]
        public long TimestampMs { get; set; }

        public int? Antenna { get; set; }

        public decimal? RssiDbm { get; set; }

        public int? Channel { get; set; }

        [Required]
        public DateTime ReadTimeLocal { get; set; }

        [Required]
        public DateTime ReadTimeUtc { get; set; }

        [MaxLength(50)]
        public string TimeZoneId { get; set; } = "UTC";

        [MaxLength(20)]
        public string ProcessResult { get; set; } = "Pending"; // Pending, Success, Duplicate, Invalid

        [MaxLength(20)]
        public string? AssignmentMethod { get; set; } // Manual, Auto, Sequential, TimeGap

        public decimal? CheckpointConfidence { get; set; }

        public bool IsMultipleEpc { get; set; } = false;

        public bool RequiresManualReview { get; set; } = false;

        public bool IsManualEntry { get; set; } = false;

        public DateTime? ManualTimeOverride { get; set; }

        public long? DuplicateOfReadingId { get; set; }

        public DateTime? ProcessedAt { get; set; }

        [MaxLength(20)]
        public string SourceType { get; set; } = "file_upload"; // file_upload, online_webhook

        public string? Notes { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual UploadBatch UploadBatch { get; set; } = null!;
        public virtual ICollection<ReadingCheckpointAssignment> ReadingCheckpointAssignments { get; set; } = new List<ReadingCheckpointAssignment>();
    }
}
