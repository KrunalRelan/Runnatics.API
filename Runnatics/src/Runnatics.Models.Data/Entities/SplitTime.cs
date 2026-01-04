namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;

    public class SplitTime
    {
        [Key]
        public int Id { get; set; }
        public int ParticipantId { get; set; }
        public int FromCheckpointId { get; set; }
        public int ToCheckpointId { get; set; }
        public TimeSpan SplitTimeValue { get; set; } // Maps to SplitTime column (time type)
        public decimal? Distance { get; set; }
        public decimal? AveragePace { get; set; }
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Computed property for milliseconds (used by service layer)
        public long SplitTimeMs => (long)SplitTimeValue.TotalMilliseconds;

        // Navigation Properties
        public virtual Participant Participant { get; set; } = null!;
        public virtual Checkpoint FromCheckpoint { get; set; } = null!;
        public virtual Checkpoint ToCheckpoint { get; set; } = null!;
    }
}