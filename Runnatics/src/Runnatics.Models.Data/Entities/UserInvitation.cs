namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Runnatics.Models.Data.Common;

    public class UserInvitation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TenantId { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; }

        [Required]
        [StringLength(50)]
        public string FirstName { get; set; }

        [Required]
        [StringLength(50)]
        public string LastName { get; set; }

        [Required]
        public UserRole Role { get; set; }

        [Required]
        [StringLength(255)]
        public string Token { get; set; }

        [Required]
        public int InvitedBy { get; set; }

        public DateTime ExpiryDate { get; set; }

        public bool IsAccepted { get; set; } = false;

        public bool IsExpired { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? AcceptedAt { get; set; }

        public int? AcceptedBy { get; set; }

        // Navigation properties
        [ForeignKey("TenantId")]
        public virtual Organization Organization { get; set; }

        [ForeignKey("InvitedBy")]
        public virtual User InvitedByUser { get; set; }

        [ForeignKey("AcceptedBy")]
        public virtual User AcceptedByUser { get; set; }
    }
}