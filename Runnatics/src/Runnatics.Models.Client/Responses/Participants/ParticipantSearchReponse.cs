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
    }
}
