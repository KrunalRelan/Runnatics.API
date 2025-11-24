using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class ImportBatch
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TenantId { get; set; }

        [Required]
        public int EventId { get; set; }

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public int TotalRecords { get; set; }

        public int SuccessCount { get; set; } = 0;

        public int ErrorCount { get; set; } = 0;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Completed, PartiallyCompleted

        [Required]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAt { get; set; }

        public string? ErrorLog { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual Event Event { get; set; } = null!;
        public virtual ICollection<ParticipantStaging> StagingRecords { get; set; } = new List<ParticipantStaging>();
        public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();
    }
}
