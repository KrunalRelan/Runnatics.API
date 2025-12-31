using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Certificates
{
    public class CertificateTemplateRequest
    {
        [Required(ErrorMessage = "Event ID is required")]
        public string EventId { get; set; } = string.Empty;

        public string? RaceId { get; set; }

        [Required(ErrorMessage = "Template name is required")]
        [MaxLength(255, ErrorMessage = "Template name cannot exceed 255 characters")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
        public string? Description { get; set; }

        public string? BackgroundImageData { get; set; }

        [Required(ErrorMessage = "Width is required")]
        [Range(1, 10000, ErrorMessage = "Width must be between 1 and 10000")]
        public int Width { get; set; } = 1754;

        [Required(ErrorMessage = "Height is required")]
        [Range(1, 10000, ErrorMessage = "Height must be between 1 and 10000")]
        public int Height { get; set; } = 1240;

        /// <summary>
        /// Indicates if this template should be the default template for the event.
        /// If set to true, any existing default template for the event will be unmarked.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        public bool IsActive { get; set; } = true;

        [Required(ErrorMessage = "At least one field is required")]
        [MinLength(1, ErrorMessage = "At least one field is required")]
        public List<CertificateFieldRequest> Fields { get; set; } = new();
    }
}
