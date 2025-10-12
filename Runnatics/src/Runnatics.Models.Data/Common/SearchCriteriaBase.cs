namespace Runnatics.Models.Data.Common
{
    public class SearchCriteriaBase
    {
        public SearchCriteriaBase()
        {
            PageNumber = 1;
            PageSize = DefaultPageSize;
            SortField = null;
            SortDirection = SortDirection.Ascending;
        }
        
        public const int DefaultPageSize = 100;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = DefaultPageSize;
        public string? SortField { get; set; }
        public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    }
}