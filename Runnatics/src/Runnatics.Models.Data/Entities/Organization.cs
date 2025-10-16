using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class Organization
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [StringLength(100)]
        public string Slug { get; set; }

        [Required]
        [StringLength(30)]
        public string Domain { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; }

        [Phone]
        [StringLength(20)]
        public string? PhoneNumber { get; set; }

        [Url]
        [StringLength(255)]
        public string? Website { get; set; }

        [StringLength(255)]
        public string? LogoUrl { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        // JSON settings
        public string? Settings { get; set; }

        // Subscription Information
        [Required]
        [StringLength(50)]
        public string SubscriptionPlan { get; set; } = "starter";

        public DateTime? SubscriptionStartDate { get; set; }

        public DateTime? SubscriptionEndDate { get; set; }

        public bool IsSubscriptionActive { get; set; } = true;

        // Organization Settings
        [StringLength(10)]
        public string TimeZone { get; set; } = "UTC";

        [StringLength(10)]
        public string Currency { get; set; } = "USD";

        [StringLength(100)]
        public string? Country { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        // Status
        public bool IsActive { get; set; } = true;

        public bool IsVerified { get; set; } = false;

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active";

        // Limits based on subscription
        public int MaxEvents { get; set; } = 5;

        public int MaxParticipantsPerEvent { get; set; } = 1000;

        public int MaxUsers { get; set; } = 3;

        // Audit Properties
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ICollection<User> Users { get; set; } = new List<User>();

        public virtual ICollection<Event> Events { get; set; } = new List<Event>();

        public virtual ICollection<UserInvitation> UserInvitations { get; set; } = new List<UserInvitation>();

        public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();

        public virtual ICollection<Chip> Chips { get; set; } = new List<Chip>();

        // Computed Properties (not mapped to database)
        [NotMapped]
        public int TotalUsers => Users?.Count(u => u.IsActive && !u.AuditProperties.IsDeleted) ?? 0;

        [NotMapped]
        public int ActiveEvents => Events?.Count(e => e.AuditProperties.IsActive && !e.AuditProperties.IsDeleted) ?? 0;

        [NotMapped]
        public int PendingInvitations => UserInvitations?.Count(i => !i.IsAccepted && !i.IsExpired && i.ExpiryDate > DateTime.UtcNow) ?? 0;

        [NotMapped]
        public string AccessUrl => $"https://{Domain}.runnatics.com";

        [NotMapped]
        public bool IsSubscriptionExpired => SubscriptionEndDate.HasValue && SubscriptionEndDate.Value < DateTime.UtcNow;

        [NotMapped]
        public int DaysUntilSubscriptionExpiry => SubscriptionEndDate.HasValue ?
            Math.Max(0, (int)(SubscriptionEndDate.Value - DateTime.UtcNow).TotalDays) : int.MaxValue;
    }
}
