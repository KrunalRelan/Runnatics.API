namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;

    public class UserSession
    {
        [Required]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(255)]
        public string TokenHash { get; set; } = string.Empty;

        [Required]
        public DateTime ExpiresAt { get; set; }

        public string? UserAgent { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        // Navigation Properties
        public virtual User User { get; set; } = null!;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
