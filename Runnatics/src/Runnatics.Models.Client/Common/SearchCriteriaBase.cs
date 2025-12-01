using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Common
{
    public class SearchCriteriaBase
    {
        public const int DefaultPageSize = 100;

        public string SearchString { get; set; } = string.Empty;

        [StringLength(50)]
        public string SortFieldName { get; set; } = string.Empty;

        public SortDirection SortDirection { get; set; }

        [Range(1, int.MaxValue)]
        public int PageNumber { get; set; }

        [Range(1, int.MaxValue)]
        public int PageSize { get; set; }

        public SearchCriteriaBase()
        {
            SortDirection = SortDirection.Ascending;
            PageNumber = 1;
            PageSize = DefaultPageSize;
        }
    }
}
