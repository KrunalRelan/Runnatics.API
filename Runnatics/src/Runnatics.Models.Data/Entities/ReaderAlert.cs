using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Reader alerts
    /// </summary>
    public class ReaderAlert
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int ReaderDeviceId { get; set; }

        public ReaderAlertType AlertType { get; set; }

        public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public string? Details { get; set; }

        public bool IsAcknowledged { get; set; } = false;

        public int? AcknowledgedByUserId { get; set; }

        public DateTime? AcknowledgedAt { get; set; }

        [MaxLength(1000)]
        public string? ResolutionNotes { get; set; }

        public bool IsResolved { get; set; } = false;

        public DateTime? ResolvedAt { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
        public virtual User? AcknowledgedByUser { get; set; }
    }
}
