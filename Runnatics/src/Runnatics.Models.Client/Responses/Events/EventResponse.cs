using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Responses.Events
{
    public class EventResponse
    {
        public string Id { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime EventDate { get; set; }

        public string TimeZone { get; set; } = string.Empty;

        public string? VenueName { get; set; }

        public string? VenueAddress { get; set; }

        public decimal? VenueLatitude { get; set; }

        public decimal? VenueLongitude { get; set; }

        public EventStatus Status { get; set; }

        public int? MaxParticipants { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        // Deprecated - kept for backward compatibility
        public string? City { get; set; } 

        public string EventOrganizerId { get; set; } = string.Empty;

        public string? EventOrganizerName { get; set; }

        // Event Settings
        public EventSettingsResponse? EventSettings { get; set; }

        // Leaderboard Settings
        public LeaderboardSettingsResponse? LeaderboardSettings { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; }

        public string EventType { get; set; } = string.Empty;
    }
}
