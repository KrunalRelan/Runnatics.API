using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Reader configuration profiles
    /// </summary>
    public class ReaderProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ProfileName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(50)]
        public string ReaderMode { get; set; } = "AutoSetDenseReader";

        [MaxLength(50)]
        public string SearchMode { get; set; } = "DualTarget";

        public byte Session { get; set; } = 2;

        public int TagPopulation { get; set; } = 32;

        public int FilterDuplicateReadsMs { get; set; } = 1000;

        public int DefaultTxPowerCdBm { get; set; } = 3000;

        public bool EnableAntennaHub { get; set; } = false;

        public bool IsDefault { get; set; } = false;

        public string? AdvancedSettingsJson { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ICollection<ReaderDevice> ReaderDevices { get; set; } = new List<ReaderDevice>();
    }
}
