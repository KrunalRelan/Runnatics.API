using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Support
{
    public class ContactUsRequestDto
    {
        [Required]
        [MaxLength(255)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string SubmitterEmail { get; set; } = string.Empty;
    }
}
