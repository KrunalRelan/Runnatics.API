using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class Device
    {
        [Key]
        public int Id { get; set; }

        public string? DeviceId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public int TenantId { get; set; }

        [MaxLength(100)]
        public string? Hostname { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(50)]
        public string? FirmwareVersion { get; set; }

        [MaxLength(50)]
        public string? ReaderModel { get; set; }

        public bool IsOnline { get; set; }

        public DateTime? LastSeenAt { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

    }
}
