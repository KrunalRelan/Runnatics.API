namespace Runnatics.Models.Client.Responses.Events
{
    public class EventSettingsResponse
    {
        public int Id { get; set; }

        public int EventId { get; set; }

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
