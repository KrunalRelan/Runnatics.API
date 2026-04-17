namespace Runnatics.Models.Client.Public
{
    public class PublicEventDetailDto : PublicEventSummaryDto
    {
        public string? FullDescription { get; set; }

        public string? Schedule { get; set; }

        public string? RouteMapUrl { get; set; }

        public List<PublicRaceCategoryDto> Races { get; set; } = [];

        public DateTime? RegistrationDeadline { get; set; }

        // Event.EventOrganizer contact — populate from EventOrganizer if available
        public string? ContactEmail { get; set; }
    }
}
