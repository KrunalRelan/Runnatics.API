using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// File upload column mappings
    /// </summary>
    public class FileUploadMapping
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string MappingName { get; set; } = string.Empty;

        public FileFormat FileFormat { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool HasHeaderRow { get; set; } = true;

        [MaxLength(5)]
        public string Delimiter { get; set; } = ",";

        [MaxLength(50)]
        public string EpcColumn { get; set; } = "epc";

        [MaxLength(50)]
        public string TimestampColumn { get; set; } = "timestamp";

        [MaxLength(100)]
        public string TimestampFormat { get; set; } = "yyyy-MM-ddTHH:mm:ss.fffZ";

        [MaxLength(50)]
        public string? AntennaPortColumn { get; set; }

        [MaxLength(50)]
        public string? RssiColumn { get; set; }

        [MaxLength(50)]
        public string? ReaderSerialColumn { get; set; }

        [MaxLength(50)]
        public string? PhaseAngleColumn { get; set; }

        [MaxLength(50)]
        public string? DopplerColumn { get; set; }

        [MaxLength(50)]
        public string? ChannelIndexColumn { get; set; }

        [MaxLength(50)]
        public string? TagSeenCountColumn { get; set; }

        [MaxLength(50)]
        public string? LatitudeColumn { get; set; }

        [MaxLength(50)]
        public string? LongitudeColumn { get; set; }

        public bool IsDefault { get; set; } = false;

        public string? AdditionalMappingsJson { get; set; }

        public int? OrganizationId { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual Organization? Organization { get; set; }
    }
}
