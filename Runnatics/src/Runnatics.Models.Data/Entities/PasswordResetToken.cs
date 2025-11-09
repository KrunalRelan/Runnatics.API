using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class PasswordResetToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(255)]
        public string TokenHash { get; set; } = string.Empty;

        [Required]
        public DateTime ExpiresAt { get; set; }

        public bool IsUsed { get; set; } = false;

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(255)]
        public string? UserAgent { get; set; }

        // Navigation Properties
        public virtual User User { get; set; } = null!;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
