namespace Runnatics.Models.Client.Requests.BibMapping
{
    public class GetEpcMappingRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public string? SearchTerm { get; set; }
        public EpcMappingStatusFilter? Status { get; set; }
    }

    public enum EpcMappingStatusFilter
    {
        All,
        Mapped,
        Unmapped
    }
}
