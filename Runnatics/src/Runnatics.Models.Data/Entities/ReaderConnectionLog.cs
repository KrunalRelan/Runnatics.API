using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Reader connection event logs
    /// </summary>
    public class ReaderConnectionLog
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int ReaderDeviceId { get; set; }

        public ReaderConnectionEventType EventType { get; set; }

        public ConnectionProtocol? ConnectionProtocol { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
    }
}
