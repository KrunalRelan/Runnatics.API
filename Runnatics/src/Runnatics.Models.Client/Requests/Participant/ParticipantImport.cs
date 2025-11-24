using Microsoft.AspNetCore.Http;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantImportRequest
    {
        public int EventId { get; set; }
        public int? RaceId { get; set; }
        public required IFormFile File { get; set; }
    }
}