namespace Runnatics.Models.Client.Requests.Events
{
    public class EventSettingsRequest
    {
        // RemoveBanner, ShowResultSummaryForRaces, UseOldData, AllowParticipantEdit are server-controlled and always false

        public bool Published { get; set; } = false;

        public bool RankOnNet { get; set; } = true;

        public bool ConfirmedEvent { get; set; } = false;

        public bool AllowNameCheck { get; set; } = true;
    }
}
