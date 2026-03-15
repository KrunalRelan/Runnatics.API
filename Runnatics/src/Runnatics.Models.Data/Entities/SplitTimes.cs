namespace Runnatics.Models.Data.Entities
{
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Entities;
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public class SplitTimes
    {
        [Key]
        public int Id { get; set; }

        // Participant and Event
        public int ParticipantId { get; set; }
        public int? EventId { get; set; }

        // Checkpoint references - REQUIRED fields define the segment
        [Required]
        public int FromCheckpointId { get; set; }  // Start of segment (previous checkpoint or race start)

        [Required]
        public int ToCheckpointId { get; set; }    // End of segment (current checkpoint)

        public int? CheckpointId { get; set; }     // Same as ToCheckpointId (for compatibility)

        // Link to normalized reading
        public int? ReadNormalizedId { get; set; }

        // Time measurements
        [Required]
        [Column(TypeName = "time(7)")]
        public TimeSpan SplitTime { get; set; }    // REQUIRED: Legacy TIME column for cumulative time

        public long? SplitTimeMs { get; set; }     // Milliseconds from race start to this checkpoint (cumulative)

        public long? SegmentTime { get; set; }     // Milliseconds from previous checkpoint (segment only)

        // Distance and pace
        public decimal? Distance { get; set; }
        public decimal? AveragePace { get; set; }
        public decimal? Pace { get; set; }         // Minutes per km

        // Rankings
        public int? Rank { get; set; }             // Overall rank at this checkpoint
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Participant Participant { get; set; } = null!;
        public virtual Checkpoint Checkpoint { get; set; } = null!;
        public virtual Checkpoint FromCheckpoint { get; set; } = null!;
        public virtual Checkpoint ToCheckpoint { get; set; } = null!;
        public virtual ReadNormalized? ReadNormalized { get; set; }
    }
}