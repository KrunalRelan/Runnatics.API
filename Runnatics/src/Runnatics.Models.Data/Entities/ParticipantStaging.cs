using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class ParticipantStaging
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ImportBatchId { get; set; }

        [Required]
        public int RowNumber { get; set; }

        // Raw CSV Data - all nullable as CSV can have missing values
        [MaxLength(50)]
        public string? Bib { get; set; }

        [MaxLength(500)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? Gender { get; set; }

        [MaxLength(100)]
        public string? AgeCategory { get; set; }

        [MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Mobile { get; set; }

        // Processing Fields
        [Required]
        [MaxLength(20)]
        public string ProcessingStatus { get; set; } = "Pending"; // Pending, Success, Error

        public string? ErrorMessage { get; set; }

        public int? ParticipantId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ImportBatch ImportBatch { get; set; } = null!;
        public virtual Participant? Participant { get; set; }
    }
}
