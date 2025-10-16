namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;

    public class Results
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EventId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid RaceCategoryId { get; set; }
        public long? FinishTime { get; set; } // Total race time in milliseconds
        public long? GunTime { get; set; } // Time from gun start
        public long? NetTime { get; set; } // Time from participant crossing start line
        public int? OverallRank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
        public string Status { get; set; } = "Finished"; // Finished, DNF, DQ
        public string? DisqualificationReason { get; set; }
        public bool IsOfficial { get; set; } = false; // Final verified result
        public bool CertificateGenerated { get; set; } = false;
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
        // Computed Properties
        public TimeSpan? FinishTimeSpan => FinishTime.HasValue ? TimeSpan.FromMilliseconds(FinishTime.Value) : null;
        public string? FormattedFinishTime => FinishTimeSpan?.ToString(@"hh\:mm\:ss");

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Participant Participant { get; set; } = null!;
        public virtual RaceCategory RaceCategory { get; set; } = null!;
    }
}