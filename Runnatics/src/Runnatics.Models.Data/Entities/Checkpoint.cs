using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;
namespace Runnatics.Models.Data.Entities
{
    public class Checkpoint
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        [Required]
        public int RaceId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; 

        [Required]
        public decimal DistanceFromStart { get; set; }
        public int DeviceId { get; set; }
        public int? ParentDeviceId { get; set; }

        public bool IsMandatory { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        //public virtual Event Event { get; set; };

        //public virtual required Race Race { get; set; }

        //public virtual ICollection<ReaderAssignment> ReaderAssignments { get; set; } = new List<ReaderAssignment>();
        //public virtual ICollection<ReadNormalized> ReadNormalized { get; set; } = new List<ReadNormalized>();
        //public virtual ICollection<SplitTime> SplitTimes { get; set; } = new List<SplitTime>();
    }
}