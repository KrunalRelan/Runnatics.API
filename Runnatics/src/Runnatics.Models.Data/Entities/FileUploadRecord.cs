using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Data.Entities
{
    /// <summary>
    /// Individual records from file uploads
    /// </summary>
    public class FileUploadRecord
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int FileUploadBatchId { get; set; }

        public int RowNumber { get; set; }

        [Required]
        [MaxLength(64)]
        public string Epc { get; set; } = string.Empty;

        public DateTime ReadTimestamp { get; set; }

        public byte? AntennaPort { get; set; }

        public decimal? RssiDbm { get; set; }

        [MaxLength(100)]
        public string? ReaderSerialNumber { get; set; }

        [MaxLength(100)]
        public string? ReaderHostname { get; set; }

        public decimal? PhaseAngleDegrees { get; set; }

        public decimal? DopplerFrequencyHz { get; set; }

        public int? ChannelIndex { get; set; }

        public int? PeakRssiCdBm { get; set; }

        public int TagSeenCount { get; set; } = 1;

        public decimal? GpsLatitude { get; set; }

        public decimal? GpsLongitude { get; set; }

        public ReadRecordStatus ProcessingStatus { get; set; } = ReadRecordStatus.Pending;

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public int? MatchedChipId { get; set; }

        public int? MatchedParticipantId { get; set; }

        public long? CreatedReadRawId { get; set; }

        public string? RawData { get; set; }

        public DateTime? ProcessedAt { get; set; }

        public AuditProperties AuditProperties { get; set; } = new AuditProperties();

        // Navigation Properties
        public virtual FileUploadBatch FileUploadBatch { get; set; } = null!;
        public virtual Chip? MatchedChip { get; set; }
        public virtual Participant? MatchedParticipant { get; set; }
    }
}
