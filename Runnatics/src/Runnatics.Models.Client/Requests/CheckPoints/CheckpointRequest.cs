using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.CheckPoints
{
    public class CheckpointRequest
    {
        [Required]
        public string EventId { get; set; }

        [Required]
        public string RaceId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public decimal DistanceFromStart { get; set; }
        public string DeviceId { get; set; }
        public string? ParentDeviceId { get; set; }
    }
}
