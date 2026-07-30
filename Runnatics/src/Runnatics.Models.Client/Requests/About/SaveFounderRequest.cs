using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.About
{
    /// <summary>Create / update payload for one founder tile.</summary>
    public class SaveFounderRequest
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Role { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        public string? PhotoBase64 { get; set; }

        public int DisplayOrder { get; set; }
    }
}
