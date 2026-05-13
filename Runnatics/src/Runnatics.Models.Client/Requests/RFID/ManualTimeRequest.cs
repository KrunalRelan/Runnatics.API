using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.RFID
{
    public class ManualTimeRequest
    {
        [Required]
        [Range(1, long.MaxValue, ErrorMessage = "FinishTimeMs must be a positive value.")]
        public long FinishTimeMs { get; set; }

        [Required]
        public string CheckpointId { get; set; }
    }
}
