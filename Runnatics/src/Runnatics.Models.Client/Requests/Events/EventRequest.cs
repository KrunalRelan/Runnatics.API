using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Events
{
    public class EventRequest
    {
        [Required]
        public string EventOrganizerId { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        // [Required]
        // [MaxLength(100)]
        // public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public DateTime EventDate { get; set; }

        [MaxLength(255)]
        public string? VenueName { get; set; }

        public string? VenueAddress { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        public string? Country { get; set; }

        public string? ZipCode { get; set; }

        public string? VenuePostalCode { get; set; }

        public string EventType { get; set; } = string.Empty;

        public decimal? VenueLatitude { get; set; }
        public decimal? VenueLongitude { get; set; }

        // Banner — base64 encoded. On create: saved as-is. On update: only saved if no banner exists.
        public string? BannerBase64 { get; set; }

        // Event Settings
        public EventSettingsRequest? EventSettings { get; set; }

        // Leaderboard Settings
        public LeaderboardSettingsRequest? LeaderboardSettings { get; set; }
    }
}
