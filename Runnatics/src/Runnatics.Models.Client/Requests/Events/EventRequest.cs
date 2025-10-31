using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Requests.Events
{
    public class EventRequest
    {
        [Required]
        public int OrganizationId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime EventDate { get; set; }

        public string TimeZone { get; set; } 

        public string? VenueName { get; set; }

        public string? VenueAddress { get; set; }

        public decimal? VenueLatitude { get; set; }
        public decimal? VenueLongitude { get; set; }

        public string Status { get; set; } 

        public int? MaxParticipants { get; set; }
        public DateTime? RegistrationDeadline { get; set; }
        public string? Settings { get; set; } // JSON
    }
}
