using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Public
{
    public class CompareParticipantsRequest
    {
        [Required]
        public string ParticipantId1 { get; set; } = string.Empty;

        [Required]
        public string ParticipantId2 { get; set; } = string.Empty;
    }
}
