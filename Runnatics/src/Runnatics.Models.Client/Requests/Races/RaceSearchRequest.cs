using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.Races
{
    public class RaceSearchRequest : SearchCriteriaBase
    {
        public RaceSearchRequest()
        {
            SortFieldName = "Id";
            SortDirection = SortDirection.Descending;
        }

        public string? Title { get; set; } 

        public string? Description { get; set; }

        public decimal? Distance { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public int? MaxParticipants { get; set; }

        public Status? Status { get; set; }

    }
}
