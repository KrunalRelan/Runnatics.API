namespace Runnatics.Models.Client.Responses.Participants
{
    public class ParticipantSearchReponse
    {
        public string? Id { get; set; }
        public string? Bib { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public bool? CheckedIn { get; set; }
        public string? ChipId { get; set; }

        /// <summary>
        /// Dictionary of checkpoint times keyed by checkpoint name (e.g., "Start", "5 KM", "Finish")
        /// Values are formatted times (HH:mm:ss) or null if not crossed
        /// </summary>
        public Dictionary<string, string?>? CheckpointTimes { get; set; }
    }
}
