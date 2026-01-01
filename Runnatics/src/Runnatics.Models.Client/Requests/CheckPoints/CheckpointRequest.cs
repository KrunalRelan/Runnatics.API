using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.CheckPoints
{
    public class CheckpointRequest
    {
        public required string DeviceId { get; set; }

        [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
        public string? Name { get; set; }

        [Required]
        public decimal DistanceFromStart { get; set; }
        public string? ParentDeviceId { get; set; }

        public bool IsMandatory { get; set; }
    }
}
