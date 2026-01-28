using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    public class ReadingCheckpointAssignment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public long ReadingId { get; set; }

        [Required]
        public int CheckpointId { get; set; }

        //public int? DetectionId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual RawRFIDReading Reading { get; set; } = null!;
        public virtual Checkpoint Checkpoint { get; set; } = null!;
    }
}
