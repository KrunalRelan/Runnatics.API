using Runnatics.Models.Data.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Key-value store for editable public-site copy (About page today; any future
    /// page section is a new ContentKey row, not a new table).
    /// Keys are dotted paths, e.g. "About.WhoWeAre", "About.Mission", "About.StoryImage".
    /// </summary>
    public class SiteContent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ContentKey { get; set; } = string.Empty;

        /// <summary>Plain text for copy; base64 data URI payload for image keys.</summary>
        public string? ContentValue { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();
    }
}
