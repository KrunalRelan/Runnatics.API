using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class ReadRaw
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int EventId { get; set; }

        [Required]
        public int ReaderDeviceId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ChipEPC { get; set; } = string.Empty;

        // Alias for compatibility with new tables
        [MaxLength(64)]
        public string? Epc { get; set; }

        public DateTime ReadTimestamp { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        public int? Rssi { get; set; } // Signal strength
        public int? AntennaPort { get; set; }
        public int? CheckpointId { get; set; }
        public bool IsProcessed { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // New columns from ALTER TABLE
        public int? FileUploadBatchId { get; set; }

        [MaxLength(50)]
        public string? Source { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
        public virtual FileUploadBatch? FileUploadBatch { get; set; }
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = [];
    }
}