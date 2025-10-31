using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Events
{
    public class EventResponse
    {
        public int Id { get; set; }

        public int OrganizationId { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime EventDate { get; set; }

        public string? City { get; set; } 

        public string? EventOrganizerName { get; set; }

        public bool IsPublished { get; set; }
    }
}
