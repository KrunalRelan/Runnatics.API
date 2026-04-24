using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.BibMapping
{
    public class GetEpcMappingRequest : SearchCriteriaBase
    {
        public EpcMappingStatusFilter? Status { get; set; }
    }

    public enum EpcMappingStatusFilter
    {
        All,
        Mapped,
        Unmapped
    }
}
