using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class ChipAssignment
    {
        public int EventId { get; set; }
        public int ParticipantId { get; set; }
        public int ChipId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UnassignedAt { get; set; }
        public Guid? AssignedByUserId { get; set; }

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Participant Participant { get; set; } = null!;
        public virtual Chip Chip { get; set; } = null!;
        public virtual User? AssignedByUser { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}


