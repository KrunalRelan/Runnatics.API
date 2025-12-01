using Microsoft.AspNetCore.Http;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantImportRequest
    {
        public string? RaceId { get; set; }
        public required IFormFile File { get; set; }
    }
}