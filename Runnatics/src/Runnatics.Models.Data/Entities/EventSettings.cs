using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class EventSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        public bool RemoveBanner { get; set; }

        public bool Published { get; set; }

        public bool RankOnNet { get; set; }

        public bool ShowResultSummaryForRaces { get; set; }

        public bool UseOldData { get; set; }

        public bool ConfirmedEvent { get; set; }

        public bool AllowNameCheck { get; set; }

        public bool AllowParticipantEdit { get; set; }

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;

        // Audit Properties
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
