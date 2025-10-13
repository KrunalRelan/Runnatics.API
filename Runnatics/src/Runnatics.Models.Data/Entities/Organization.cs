using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class Organization
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Domain { get; set; }

        [MaxLength(50)]
        public string TimeZone { get; set; } = "Asia/Kolkata";

        public string? Settings { get; set; } // JSON

        [MaxLength(50)]
        public string? SubscriptionPlan { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ICollection<User> Users { get; set; } = new List<User>();
        public virtual ICollection<Event> Events { get; set; } = new List<Event>();
        public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();
        public virtual ICollection<Chip> Chips { get; set; } = new List<Chip>();
        public virtual ICollection<ReaderDevice> ReaderDevices { get; set; } = new List<ReaderDevice>();
    }
}
