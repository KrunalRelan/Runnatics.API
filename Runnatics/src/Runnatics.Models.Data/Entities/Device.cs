using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class Device
    {
        [Key]
        public int Id { get; set; }

        public string DeviceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public int TenantId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

    }
}
