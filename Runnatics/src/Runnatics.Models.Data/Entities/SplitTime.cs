namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Entities;

    public class SplitTime
    {
        public Guid EventId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid CheckpointId { get; set; }
        public Guid? ReadNormalizedId { get; set; }
        public long SplitTimeMs { get; set; } // Milliseconds from start to this checkpoint
        public long? SegmentTime { get; set; } // Milliseconds from previous checkpoint
        public decimal? Pace { get; set; } // Minutes per km
        public int? Rank { get; set; } // Overall rank at this checkpoint
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Participant Participant { get; set; } = null!;
        public virtual Checkpoint Checkpoint { get; set; } = null!;
        public virtual ReadNormalized? ReadNormalized { get; set; }
    }
}