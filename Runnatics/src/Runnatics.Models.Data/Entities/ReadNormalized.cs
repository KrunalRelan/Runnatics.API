namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;

    public class ReadNormalized
    {
        public Guid EventId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid CheckpointId { get; set; }
        public long? RawReadId { get; set; }
        public DateTime ChipTime { get; set; } // Exact chip read time
        public long? GunTime { get; set; } // Milliseconds from race start
        public long? NetTime { get; set; } // Milliseconds from participant start
        public bool IsManualEntry { get; set; } = false;
        public string? ManualEntryReason { get; set; }
        public Guid? CreatedByUserId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Participant Participant { get; set; } = null!;
        public virtual Checkpoint Checkpoint { get; set; } = null!;
        public virtual ReadRaw? RawRead { get; set; }
        public virtual User? CreatedByUser { get; set; }
    }
}