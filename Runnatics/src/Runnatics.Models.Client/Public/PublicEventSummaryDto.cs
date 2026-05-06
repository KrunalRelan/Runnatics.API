namespace Runnatics.Models.Client.Public
{
    public class PublicEventSummaryDto
    {
        public string EncryptedId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? City { get; set; }

        public string? State { get; set; }

        public DateTime EventDate { get; set; }

        public string? HeroImageUrl { get; set; }

        public string? BannerBase64 { get; set; }

        // Truncated to 200 chars max at the mapping layer
        public string? Description { get; set; }

        // Race.Title values for the event's races
        public List<string> RaceCategories { get; set; } = [];

        public int? ParticipantCount { get; set; }

        public bool RegistrationOpen { get; set; }

        public string? RegistrationUrl { get; set; }

        // Maps from Event.VenueName
        public string? Venue { get; set; }

        /// <summary>
        /// True when at least one published race has ShowResultTable enabled.
        /// Used to conditionally show the "View Result →" button on public event tiles.
        /// </summary>
        public bool HasPublishedResults { get; set; }
    }
}
