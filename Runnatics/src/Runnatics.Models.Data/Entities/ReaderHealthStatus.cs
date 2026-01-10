using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Reader health status tracking
    /// </summary>
    public class ReaderHealthStatus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReaderDeviceId { get; set; }

        public bool IsOnline { get; set; } = false;

        public DateTime? LastHeartbeat { get; set; }

        public decimal? CpuTemperatureCelsius { get; set; }

        public decimal? AmbientTemperatureCelsius { get; set; }

        public ReaderMode ReaderMode { get; set; } = ReaderMode.Offline;

        [MaxLength(50)]
        public string? FirmwareVersion { get; set; }

        public long TotalReadsToday { get; set; } = 0;

        public DateTime? LastReadTimestamp { get; set; }

        public long? UptimeSeconds { get; set; }

        public decimal? MemoryUsagePercent { get; set; }

        public decimal? CpuUsagePercent { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual ReaderDevice ReaderDevice { get; set; } = null!;
    }
}
