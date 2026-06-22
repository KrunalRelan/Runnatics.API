using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Durable manual-time correction. The authoritative INPUT layer for manual times:
    /// it is NOT touched by ClearProcessedData / reprocess, and is re-applied onto
    /// ReadNormalized by Phase 2.4 on every rebuild (reprocess, clear+reprocess, race move).
    /// Removed only by an explicit revert (soft-delete) or by a race move (the CheckpointId
    /// is race-specific and meaningless after a move). One ACTIVE row per (participant,
    /// checkpoint) — enforced by a filtered unique index (WHERE IsDeleted = 0).
    /// </summary>
    public class ManualTimeOverride
    {
        [Key]
        public int Id { get; set; }

        public int EventId { get; set; }

        public int RaceId { get; set; }

        public int ParticipantId { get; set; }

        public int CheckpointId { get; set; }

        /// <summary>The corrected crossing instant, in UTC (same basis as ReadNormalized.ChipTime).</summary>
        public DateTime ManualCrossingUtc { get; set; }

        /// <summary>
        /// When set, the override is a "chosen read" (the operator picked an existing hardware read
        /// at this checkpoint, rather than typing a time). The id of that <see cref="RawRFIDReading"/>.
        /// Phase 2.4 then sets ReadNormalized.RawReadId = this value and IsManualEntry = false, so the
        /// chosen read highlights as normalized. NULL = typed manual time (legacy behaviour).
        /// Deliberately NOT a FK: raw reads can be hard-deleted on clear-with-keepUploads=false, and the
        /// override must survive that — apply then degrades to ManualCrossingUtc (timing stays correct,
        /// only the read-highlight is lost).
        /// </summary>
        public long? ChosenRawReadId { get; set; }

        public string? Reason { get; set; }

        public int? CreatedByUserId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Race Race { get; set; } = null!;
        public virtual Participant Participant { get; set; } = null!;
        public virtual Checkpoint Checkpoint { get; set; } = null!;
        public virtual User? CreatedByUser { get; set; }
    }
}
