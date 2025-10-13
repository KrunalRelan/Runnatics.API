using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
namespace Runnatics.Models.Data.Entities
{
    public class Checkpoint
    {
        [Required]
        public Guid EventId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "Start", "5K Split", "Finish"

        [Required]
        [MaxLength(20)]
        public string Type { get; set; } = string.Empty; // "Start", "Split", "Finish"

        [Required]
        public decimal DistanceKm { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public int MinGapMs { get; set; } = 1000; // Minimum gap between reads in milliseconds
        public bool IsActive { get; set; } = true;
        public int? SortOrder { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
        
        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual ICollection<ReaderAssignment> ReaderAssignments { get; set; } = new List<ReaderAssignment>();
        public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = new List<ReadNormalized>();
        public virtual ICollection<SplitTime> SplitTimes { get; set; } = new List<SplitTime>();
    }
}