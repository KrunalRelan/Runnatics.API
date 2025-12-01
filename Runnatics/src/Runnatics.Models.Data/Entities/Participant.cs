using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class Participant
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int TenantId { get; set; }

        [Required]
        public int EventId { get; set; }

        [Required]
        public int RaceId { get; set; }

        public int? ImportBatchId { get; set; }

        [MaxLength(20)]
        public string? BibNumber { get; set; }

        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [MaxLength(10)]
        public string? Gender { get; set; } // "Male", "Female", "Other"

        [MaxLength(50)]
        public string? AgeCategory { get; set; } // "18-29", "30-39", etc.

        [MaxLength(100)]
        public string? Country { get; set; }

        [MaxLength(100)]
        public string? State { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(200)]
        public string? EmergencyContactName { get; set; }

        [MaxLength(20)]
        public string? EmergencyContactPhone { get; set; }

        public string? MedicalConditions { get; set; }

        [MaxLength(10)]
        public string? TShirtSize { get; set; }

        public DateTime? RegistrationDate { get; set; } = DateTime.UtcNow;

        [MaxLength(20)]
        public string Status { get; set; } = "Registered"; // Registered, CheckedIn, Started, Finished, DNF, DQ

        public string? Notes { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Computed Property
        public string FullName => $"{FirstName} {LastName}";
        public int? Age => DateOfBirth.HasValue ? DateTime.Now.Year - DateOfBirth.Value.Year : null;

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual Event Event { get; set; } = null!;
        public virtual Race Race { get; set; } = null!;
        public virtual ImportBatch? ImportBatch { get; set; }
        public virtual ICollection<ChipAssignment> ChipAssignments { get; set; } = new List<ChipAssignment>();
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = new List<ReadNormalized>();
        public virtual ICollection<SplitTime> SplitTimes { get; set; } = new List<SplitTime>();
        public virtual Results? Result { get; set; }
        public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}