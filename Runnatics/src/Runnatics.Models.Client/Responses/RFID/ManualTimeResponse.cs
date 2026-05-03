namespace Runnatics.Models.Client.Responses.RFID
{
    public class ManualTimeResponse
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public long FinishTimeMs { get; set; }
        public string FinishTime { get; set; } = string.Empty;
        public int? OverallRank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
        public int TotalFinishers { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
