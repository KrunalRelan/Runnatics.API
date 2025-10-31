using Runnatics.Models.Client.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Events
{
    public class EventSearchRequest : SearchCriteriaBase
    {
        public EventSearchRequest()
        {
            SortFieldName = "EventDate";
            SortDirection = SortDirection.Descending;
        }

        [Range(1, int.MaxValue)]
        public int Id { get; set; } 

        public string? Name { get; set; }

        [Required]
        public DateTime? EventDateTo { get; set; }

        public DateTime? EventDateFrom { get; set; }

        public string? Status { get; set; }
    }
}
