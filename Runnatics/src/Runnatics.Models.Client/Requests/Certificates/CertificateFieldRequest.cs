using Runnatics.Models.Data.Enumerations;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Certificates
{
    public class CertificateFieldRequest
    {
        [Required]
        public CertificateFieldType FieldType { get; set; }

        [MaxLength(1000)]
        public string? Content { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "X coordinate must be positive")]
        public int XCoordinate { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Y coordinate must be positive")]
        public int YCoordinate { get; set; }

        [Required]
        [MaxLength(100)]
        public string Font { get; set; } = "Arial";

        [Required]
        [Range(1, 500, ErrorMessage = "Font size must be between 1 and 500")]
        public int FontSize { get; set; } = 12;

        [Required]
        [RegularExpression(@"^[0-9A-Fa-f]{6}$", ErrorMessage = "Font color must be a 6-digit hex code")]
        public string FontColor { get; set; } = "000000";

        [Range(0, int.MaxValue, ErrorMessage = "Width must be positive")]
        public int? Width { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Height must be positive")]
        public int? Height { get; set; }

        [RegularExpression(@"^(left|center|right)$", ErrorMessage = "Alignment must be 'left', 'center', or 'right'")]
        public string? Alignment { get; set; } = "left";

        [RegularExpression(@"^(normal|bold)$", ErrorMessage = "Font weight must be 'normal' or 'bold'")]
        public string? FontWeight { get; set; } = "normal";

        [RegularExpression(@"^(normal|italic)$", ErrorMessage = "Font style must be 'normal' or 'italic'")]
        public string? FontStyle { get; set; } = "normal";
    }
}
