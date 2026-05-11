using Runnatics.Models.Client.Common;
namespace Runnatics.Models.Client.Requests.Events
{
    public class EventSearchRequest : SearchCriteriaBase
    {
        public EventSearchRequest()
        {
            SortFieldName = "EventDate";
            SortDirection = SortDirection.Ascending;
        }

        /// <summary>
        /// Event name for partial match search (optional)
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Start date for date range filter (optional)
        /// </summary>
        public DateTime? EventDateFrom { get; set; }

        /// <summary>
        /// End date for date range filter (optional)
        /// </summary>
        public DateTime? EventDateTo { get; set; }

        /// <summary>
        /// Event status filter (optional)
        /// </summary>
        public EventStatus? Status { get; set; }
    }
}
