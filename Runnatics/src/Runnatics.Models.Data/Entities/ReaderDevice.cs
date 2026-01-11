namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Enumerations;

    public class ReaderDevice
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int TenantId { get; set; }

        [Required]
        [MaxLength(100)]
        public string SerialNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Model { get; set; } // "Impinj R700"

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(17)]
        public string? MacAddress { get; set; }

        [MaxLength(100)]
        public string? Hostname { get; set; }

        [MaxLength(50)]
        public string? FirmwareVersion { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Offline"; // Online, Offline, Error

        public DateTime? LastHeartbeat { get; set; }
        public int? PowerLevel { get; set; } // dBm
        public int AntennaCount { get; set; } = 4;
        public string? Notes { get; set; }

        // Connection settings
        public ConnectionType? ConnectionType { get; set; }

        /// <summary>
        /// LLRP port (default 5084)
        /// </summary>
        public int? LlrpPort { get; set; } = 5084;

        /// <summary>
        /// REST API port (default 443)
        /// </summary>
        public int? RestApiPort { get; set; } = 443;

        /// <summary>
        /// Username for reader authentication
        /// </summary>
        [MaxLength(100)]
        public string? Username { get; set; }

        /// <summary>
        /// Password hash for reader authentication
        /// </summary>
        [MaxLength(255)]
        public string? PasswordHash { get; set; }

        /// <summary>
        /// Reader model type (e.g., "Impinj R700")
        /// </summary>
        [MaxLength(50)]
        public string? ReaderModel { get; set; }

        /// <summary>
        /// Reference to reader profile/configuration
        /// </summary>
        public int? ProfileId { get; set; }

        /// <summary>
        /// Reference to assigned checkpoint
        /// </summary>
        public int? CheckpointId { get; set; }

        /// <summary>
        /// Reference to assigned race
        /// </summary>
        public int? RaceId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual ReaderProfile? Profile { get; set; }
        public virtual Checkpoint? Checkpoint { get; set; }
        public virtual Race? Race { get; set; }
        public virtual ReaderHealthStatus? HealthStatus { get; set; }
        public virtual ICollection<ReaderAssignment> ReaderAssignments { get; set; } = new List<ReaderAssignment>();
        public virtual ICollection<ReadRaw> ReadRaws { get; set; } = new List<ReadRaw>();
        public virtual ICollection<ReaderAntenna> ReaderAntennas { get; set; } = new List<ReaderAntenna>();
        public virtual ICollection<ReaderConnectionLog> ReaderConnectionLogs { get; set; } = new List<ReaderConnectionLog>();
        public virtual ICollection<ReaderAlert> ReaderAlerts { get; set; } = new List<ReaderAlert>();
    }
}