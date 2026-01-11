using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Reader antenna configurations
    /// </summary>
    public class ReaderAntenna
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReaderDeviceId { get; set; }

        public byte AntennaPort { get; set; }

        [MaxLength(100)]
        public string? AntennaName { get; set; }

        public int TxPowerCdBm { get; set; } = 3000;

        public int RxSensitivityCdBm { get; set; } = -7000;

        public bool IsEnabled { get; set; } = true;

        public int? CheckpointId { get; set; }

        public AntennaPosition? Position { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
        public virtual Checkpoint? Checkpoint { get; set; }
    }
}
