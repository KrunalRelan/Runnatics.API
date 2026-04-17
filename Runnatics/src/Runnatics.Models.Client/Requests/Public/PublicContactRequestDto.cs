using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Public
{
    public class PublicContactRequestDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        [MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        // Optional — links this query to a specific event
        public string? EventName { get; set; }
    }
}
