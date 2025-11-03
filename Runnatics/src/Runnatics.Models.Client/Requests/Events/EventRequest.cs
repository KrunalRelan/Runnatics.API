using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Events
{
    public class EventRequest
    {
        [Required]
        public int OrganizationId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public DateTime EventDate { get; set; }

        [MaxLength(50)]
        public string TimeZone { get; set; } = "Asia/Kolkata";

        [MaxLength(255)]
        public string? VenueName { get; set; }

        public string? VenueAddress { get; set; }

        public decimal? VenueLatitude { get; set; }
        public decimal? VenueLongitude { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Draft";

        public int? MaxParticipants { get; set; }
        public DateTime? RegistrationDeadline { get; set; }

        // Event Settings
        public EventSettingsRequest? EventSettings { get; set; }

        // Leaderboard Settings
        public LeaderboardSettingsRequest? LeaderboardSettings { get; set; }

        // Audit
        public int? CreatedBy { get; set; }
    }
}
