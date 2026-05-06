using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.Public
{
    /// <summary>
    /// Request for the public events listing.
    /// SearchString maps to the free-text query (q).
    /// PageNumber and PageSize are inherited from SearchCriteriaBase.
    /// </summary>
    public class GetPublicEventsRequest : SearchCriteriaBase
    {
        public GetPublicEventsRequest()
        {
            PageSize = 12;
        }

        /// <summary>Filter by event status: "upcoming" (default) or "past".</summary>
        public string? Status { get; set; } = "upcoming";

        /// <summary>Filter by city name (optional).</summary>
        public string? City { get; set; }

        /// <summary>Filter by calendar year (optional).</summary>
        public int? Year { get; set; }

        /// <summary>Override total items returned regardless of PageSize (optional).</summary>
        public int? Take { get; set; }
    }
}
