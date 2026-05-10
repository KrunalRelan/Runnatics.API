namespace Runnatics.Models.Client.Requests.Public
{
    public class SearchParticipantsRequest
    {
        public string? SearchString { get; set; }
        public string? EncryptedEventId { get; set; }
        public string? EncryptedRaceId { get; set; }
    }
}
