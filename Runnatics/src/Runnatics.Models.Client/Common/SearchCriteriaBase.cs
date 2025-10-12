using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Common
{
    public class SearchCriteriaBase
    {
        public const int DefaultPageSize = 100;

        [StringLength(50)]
        public string SortFieldName { get; set; }

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
