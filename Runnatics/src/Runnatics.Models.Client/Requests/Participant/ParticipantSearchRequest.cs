using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class ParticipantSearchRequest : SearchCriteriaBase
    {
        public RaceStatus? Status { get; set; }

        public Gender? Gender { get; set; }

        public string? Category { get; set; }

    }
}
