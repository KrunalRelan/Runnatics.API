namespace Runnatics.Models.Client.Responses.Events
{
    public class EventSettingsResponse
    {
        public string Id { get; set; } = string.Empty;

        public string EventId { get; set; } = string.Empty;

        public bool RemoveBanner { get; set; }

        public bool Published { get; set; }

        public bool RankOnNet { get; set; }

        public bool ShowResultSummaryForRaces { get; set; }

        public bool UseOldData { get; set; }

        public bool ConfirmedEvent { get; set; }

        public bool AllowNameCheck { get; set; }

        public bool AllowParticipantEdit { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
