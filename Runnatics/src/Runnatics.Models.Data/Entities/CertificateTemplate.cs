using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    public class CertificateTemplate
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        public int? RaceId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// URL path for background image (for future Azure Blob Storage integration)
        /// </summary>
        [MaxLength(500)]
        public string? BackgroundImageUrl { get; set; }

        /// <summary>
        /// Base64 encoded background image data stored in database
        /// </summary>
        public string? BackgroundImageData { get; set; }

        [Required]
        public int Width { get; set; } = 1754;

        [Required]
        public int Height { get; set; } = 1240;

        /// <summary>
        /// Indicates if this template is the default template for the event.
        /// Only one template per event can be marked as default.
        /// </summary>
        [Required]
        public bool IsDefault { get; set; } = false;

        //public bool IsActive { get; set; } = true;

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Event Event { get; set; } = null!;
        public virtual Race? Race { get; set; }
        public virtual ICollection<CertificateField> Fields { get; set; } = new List<CertificateField>();
    }
}
