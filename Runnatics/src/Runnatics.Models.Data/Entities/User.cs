using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int TenantId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? PasswordHash { get; set; }

        [MaxLength(100)]
        public string? FirstName { get; set; }

        [MaxLength(100)]
        public string? LastName { get; set; }

        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = string.Empty; // Admin, Ops, Support, ReadOnly

        public DateTime? LastLoginAt { get; set; }
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
    }
}
