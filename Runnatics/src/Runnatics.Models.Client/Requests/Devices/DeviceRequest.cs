using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Devices
{
    public class DeviceRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? DeviceMacAddress { get; set; }

        [MaxLength(100)]
        public string? Hostname { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(50)]
        public string? FirmwareVersion { get; set; }

        [MaxLength(50)]
        public string? ReaderModel { get; set; }
    }
}
