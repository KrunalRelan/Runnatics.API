using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// File upload batch information
    /// </summary>
    public class FileUploadBatch
    {
        [Key]
        public int Id { get; set; }

        public Guid BatchGuid { get; set; } = Guid.NewGuid();

        [Required]
        public int RaceId { get; set; }

        public int? EventId { get; set; }

        public int? ReaderDeviceId { get; set; }

        public int? CheckpointId { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string StoredFileName { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        public FileFormat FileFormat { get; set; }

        public FileProcessingStatus ProcessingStatus { get; set; } = FileProcessingStatus.Pending;

        public DateTime? ProcessingStartedAt { get; set; }

        public DateTime? ProcessingCompletedAt { get; set; }

        public int TotalRecords { get; set; } = 0;

        public int ProcessedRecords { get; set; } = 0;

        public int MatchedRecords { get; set; } = 0;

        public int DuplicateRecords { get; set; } = 0;

        public int ErrorRecords { get; set; } = 0;

        [MaxLength(2000)]
        public string? ErrorMessage { get; set; }

        public string? ProcessingLog { get; set; }

        [MaxLength(64)]
        public string? FileHash { get; set; }

        [Required]
        public int UploadedByUserId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Race Race { get; set; } = null!;
        public virtual Event? Event { get; set; }
        public virtual ReaderDevice? ReaderDevice { get; set; }
        public virtual Checkpoint? Checkpoint { get; set; }
        public virtual User UploadedByUser { get; set; } = null!;
        public virtual ICollection<FileUploadRecord> FileUploadRecords { get; set; } = new List<FileUploadRecord>();
        public virtual ICollection<ReadQueueItem> ReadQueueItems { get; set; } = new List<ReadQueueItem>();
    }
}
