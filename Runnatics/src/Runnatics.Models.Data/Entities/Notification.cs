namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;

    public class Notification
    {
        [Key]
        public int Id { get; set; }
        
        public Guid? EventId { get; set; }
        public Guid? ParticipantId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Type { get; set; } = string.Empty; // SMS, Email, Push

        [Required]
        [MaxLength(255)]
        public string Recipient { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Subject { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Sent, Failed, Delivered

        public DateTime? SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; } = 0;

        // Navigation Properties
        public virtual Event? Event { get; set; }
        public virtual Participant? Participant { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
