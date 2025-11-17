using Runnatics.Models.Data.Entities;

namespace Runnatics.Models.Client.Responses.Races
{
    public class RaceResponse
    {
        public int Id { get; set; }
        public int EventId { get; set; }

        public string? Title { get; set; } 

        public string? Description { get; set; }

        public decimal? Distance { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public int? MaxParticipants { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; }

        // Navigation Properties
        public RaceSettingsResponse? RaceSettings { get; set; }

        public Event Event { get; set; } = null!;

    }
}
