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

        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime EventDate { get; set; }

        public string TimeZone { get; set; } = string.Empty;

        public string? VenueName { get; set; }

        public string? VenueAddress { get; set; }

        public decimal? VenueLatitude { get; set; }

        public decimal? VenueLongitude { get; set; }

        public string Status { get; set; } = string.Empty;

        public int? MaxParticipants { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        // Deprecated - kept for backward compatibility
        public string? City { get; set; } 

        public string? EventOrganizerName { get; set; }

        [Obsolete("Use EventSettings.Published instead")]
        public bool IsPublished { get; set; }

        // Event Settings
        public EventSettingsResponse? EventSettings { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; }
    }
}
