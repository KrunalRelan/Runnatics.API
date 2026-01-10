using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Queue for processing RFID reads
    /// </summary>
    public class ReadQueueItem
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string Epc { get; set; } = string.Empty;

        public int? ReaderDeviceId { get; set; }

        public byte? AntennaPort { get; set; }

        public DateTime ReadTimestamp { get; set; }

        public decimal? RssiDbm { get; set; }

        public int? RaceId { get; set; }

        public int? CheckpointId { get; set; }

        [MaxLength(50)]
        public string Source { get; set; } = "realtime";

        public int? FileUploadBatchId { get; set; }

        public ReadRecordStatus ProcessingStatus { get; set; } = ReadRecordStatus.Pending;

        public DateTime? ProcessedAt { get; set; }

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public byte RetryCount { get; set; } = 0;

        public byte MaxRetries { get; set; } = 3;

        public byte Priority { get; set; } = 5;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ReaderDevice? ReaderDevice { get; set; }
        public virtual Race? Race { get; set; }
        public virtual Checkpoint? Checkpoint { get; set; }
        public virtual FileUploadBatch? FileUploadBatch { get; set; }
    }
}
