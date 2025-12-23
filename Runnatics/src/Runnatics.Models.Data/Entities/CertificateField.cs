using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class CertificateField
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TemplateId { get; set; }

        [Required]
        public CertificateFieldType FieldType { get; set; }

        [MaxLength(1000)]
        public string? Content { get; set; }

        [Required]
        public int XCoordinate { get; set; }

        [Required]
        public int YCoordinate { get; set; }

        [Required]
        [MaxLength(100)]
        public string Font { get; set; } = "Arial";

        [Required]
        public int FontSize { get; set; } = 12;

        [Required]
        [MaxLength(7)]
        public string FontColor { get; set; } = "000000";

        public int? Width { get; set; }

        public int? Height { get; set; }

        [MaxLength(20)]
        public string Alignment { get; set; } = "left";

        [MaxLength(20)]
        public string FontWeight { get; set; } = "normal";

        [MaxLength(20)]
        public string FontStyle { get; set; } = "normal";

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();


        // Navigation Properties
        public virtual CertificateTemplate CertificateTemplate { get; set; } = null!;
    }
}
