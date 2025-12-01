using Runnatics.Models.Client.Common;
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantSearchRequest : SearchCriteriaBase
    {
        public RaceStatus? Status { get; set; }

        public string? Category { get; set; }

    }
}
