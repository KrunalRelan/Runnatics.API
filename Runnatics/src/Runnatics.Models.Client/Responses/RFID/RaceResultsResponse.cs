namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response containing race results with participant checkpoint data
    /// </summary>
    public class RaceResultsResponse
    {
        public RaceInfoResponse RaceInfo { get; set; } = new();
        public List<RaceParticipantResultResponse> Results { get; set; } = new();
    }
}
