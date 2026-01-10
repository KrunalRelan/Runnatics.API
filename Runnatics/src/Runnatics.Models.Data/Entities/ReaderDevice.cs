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

        // New columns from ALTER TABLE
        public ConnectionType? ConnectionType { get; set; }
        public int? LlrpPort { get; set; }
        public int? RestApiPort { get; set; }

        [MaxLength(100)]
        public string? Username { get; set; }

        [MaxLength(255)]
        public string? PasswordHash { get; set; }

        [MaxLength(50)]
        public string? ReaderModel { get; set; }

        public int? ProfileId { get; set; }
        public int? CheckpointId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual ReaderProfile? Profile { get; set; }
        public virtual Checkpoint? Checkpoint { get; set; }
        public virtual ReaderHealthStatus? HealthStatus { get; set; }
        public virtual ICollection<ReaderAssignment> ReaderAssignments { get; set; } = new List<ReaderAssignment>();
        public virtual ICollection<ReadRaw> ReadRaws { get; set; } = new List<ReadRaw>();
        public virtual ICollection<ReaderAntenna> ReaderAntennas { get; set; } = new List<ReaderAntenna>();
        public virtual ICollection<ReaderConnectionLog> ReaderConnectionLogs { get; set; } = new List<ReaderConnectionLog>();
        public virtual ICollection<ReaderAlert> ReaderAlerts { get; set; } = new List<ReaderAlert>();
    }
}