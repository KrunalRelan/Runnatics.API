namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Race information including checkpoints
    /// </summary>
    public class RaceInfoResponse
    {
        public string RaceId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string RaceName { get; set; } = string.Empty;
        public decimal? RaceDistance { get; set; }
        public DateTime? StartTime { get; set; }
        public int TotalParticipants { get; set; }
        public int FinishedParticipants { get; set; }
        public List<CheckpointInfoResponse> Checkpoints { get; set; } = new();
    }
}
