using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

        // File upload support
        public int? FileUploadBatchId { get; set; }

        /// <summary>
        /// Source of the read: "realtime" or "fileupload"
        /// </summary>
        [MaxLength(50)]
        public string? Source { get; set; } = "realtime";

        /// <summary>
        /// Phase angle in degrees (RFID signal characteristic)
        /// </summary>
        [Column(TypeName = "decimal(6,2)")]
        public decimal? PhaseAngleDegrees { get; set; }

        /// <summary>
        /// Doppler frequency in Hz (indicates tag movement)
        /// </summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal? DopplerFrequencyHz { get; set; }

        /// <summary>
        /// Channel index used for the read
        /// </summary>
        public int? ChannelIndex { get; set; }

        /// <summary>
        /// Number of times tag was seen (for aggregated reads)
        /// </summary>
        public int TagSeenCount { get; set; } = 1;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
        public virtual FileUploadBatch? FileUploadBatch { get; set; }
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = [];
    }
}