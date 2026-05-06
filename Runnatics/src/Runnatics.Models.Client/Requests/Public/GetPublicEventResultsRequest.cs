using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.Public
{
    /// <summary>
    /// Request for the public event results page.
    /// SearchString maps to the free-text participant/bib search (q).
    /// PageNumber and PageSize are inherited from SearchCriteriaBase.
    /// </summary>
    public class GetPublicEventResultsRequest : SearchCriteriaBase
    {
        public GetPublicEventResultsRequest()
        {
            PageSize = 50;
        }

        /// <summary>Filter by race title (optional).</summary>
        public string? Race { get; set; }

        /// <summary>Filter by gender (optional).</summary>
        public string? Gender { get; set; }
    }
}
