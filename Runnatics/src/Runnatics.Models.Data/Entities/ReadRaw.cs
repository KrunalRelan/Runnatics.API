using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class ReadRaw
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public Guid EventId { get; set; }

        [Required]
        public Guid ReaderDeviceId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ChipEPC { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; }

        public int? Rssi { get; set; } // Signal strength
        public int? AntennaPort { get; set; }
        public bool IsProcessed { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = new List<ReadNormalized>();
    }
}