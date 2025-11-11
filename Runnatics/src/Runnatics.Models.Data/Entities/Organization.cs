using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.EventOrganizers;

namespace Runnatics.Models.Data.Entities
{
    public class Organization
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

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

        // Subscription Information
        //[Required]
        //[StringLength(50)]
        //public string SubscriptionPlan { get; set; } = "starter";

        //public DateTime? SubscriptionStartDate { get; set; }

        //public DateTime? SubscriptionEndDate { get; set; }

        //public bool IsSubscriptionActive { get; set; } = true;

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
        public int TotalUsers => Users?.Count(u => u.AuditProperties.IsActive && !u.AuditProperties.IsDeleted) ?? 0;

        [NotMapped]
        public int ActiveEvents => Events?.Count(e => e.AuditProperties.IsActive && !e.AuditProperties.IsDeleted) ?? 0;

        [NotMapped]
        public int PendingInvitations => UserInvitations?.Count(i => !i.IsAccepted && !i.IsExpired && i.ExpiryDate > DateTime.UtcNow) ?? 0;

        public virtual ICollection<EventOrganizer> EventOrganizers { get; set; } = [];
        //[NotMapped]
        //public string AccessUrl => $"https://{Domain}.runnatics.com";

        //[NotMapped]
        //public bool IsSubscriptionExpired => SubscriptionEndDate.HasValue && SubscriptionEndDate.Value < DateTime.UtcNow;

        //[NotMapped]
        //public int DaysUntilSubscriptionExpiry => SubscriptionEndDate.HasValue ?
        //    Math.Max(0, (int)(SubscriptionEndDate.Value - DateTime.UtcNow).TotalDays) : int.MaxValue;
    }
}
