using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.FileUpload
{
    /// <summary>
    /// DTO for individual file upload records
    /// </summary>
    public class FileUploadRecordDto
    {
        public long Id { get; set; }
        public int RowNumber { get; set; }
        public string Epc { get; set; } = string.Empty;
        public DateTime ReadTimestamp { get; set; }
        public byte? AntennaPort { get; set; }
        public decimal? RssiDbm { get; set; }
        public ReadRecordStatus Status { get; set; }
        public string StatusText => Status.ToString();
        public string? ErrorMessage { get; set; }
        public int? MatchedChipId { get; set; }
        public string? MatchedChipEpc { get; set; }
        public int? MatchedParticipantId { get; set; }
        public string? MatchedParticipantName { get; set; }
        public string? MatchedBibNumber { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
