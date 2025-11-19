namespace Runnatics.Models.Client.Requests.Events
{
    public class EventSettingsRequest
    {
        public bool RemoveBanner { get; set; } = false;

        public bool Published { get; set; } = false;

        public bool RankOnNet { get; set; } = true;

        public bool ShowResultSummaryForRaces { get; set; } = true;

        public bool UseOldData { get; set; } = false;

        public bool ConfirmedEvent { get; set; } = false;

        public bool AllowNameCheck { get; set; } = true;

        public bool AllowParticipantEdit { get; set; } = true;
    }
}
