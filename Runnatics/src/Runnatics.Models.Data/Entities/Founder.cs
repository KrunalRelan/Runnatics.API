using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// A founder tile on the public About page (replaces the hardcoded
    /// "Our Services" tiles). Photo is base64 in-DB, same as event banners.
    /// </summary>
    public class Founder
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Designation shown under the name, e.g. "Co-Founder".</summary>
        [MaxLength(200)]
        public string? Role { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        /// <summary>Base64 encoded photo stored in database.</summary>
        public string? PhotoBase64 { get; set; }

        public int DisplayOrder { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
