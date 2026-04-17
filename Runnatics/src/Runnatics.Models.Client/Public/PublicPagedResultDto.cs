namespace Runnatics.Models.Client.Public
{
    // PagingList<T> in Common only carries TotalCount (it extends List<T>).
    // This DTO adds page navigation metadata needed by public endpoints.
    public class PublicPagedResultDto<T>
    {
        public List<T> Items { get; set; } = [];

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalCount { get; set; }

        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        public bool HasNext => Page < TotalPages;

        public bool HasPrevious => Page > 1;
    }
}
