namespace Runnatics.Models.Data.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Runnatics.Models.Data.Common;
    using Runnatics.Models.Data.Entities;
    public class ReaderDevice
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public Guid OrganizationId { get; set; }

        [Required]
        [MaxLength(100)]
        public string SerialNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Model { get; set; } // "Impinj R700"

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(17)]
        public string? MacAddress { get; set; }

        [MaxLength(50)]
        public string? FirmwareVersion { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Offline"; // Online, Offline, Error

        public DateTime? LastHeartbeat { get; set; }
        public int? PowerLevel { get; set; } // dBm
        public int AntennaCount { get; set; } = 4;
        public string? Notes { get; set; }
        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
        // Navigation Properties
        public virtual Organization Organization { get; set; } = null!;
        public virtual ICollection<ReaderAssignment> ReaderAssignments { get; set; } = new List<ReaderAssignment>();
        public virtual ICollection<ReadRaw> ReadRaws { get; set; } = new List<ReadRaw>();
    }
}