namespace Runnatics.Models.Client.Responses.Participants
{
    public class Participant
    {
        public int Id { get; set; }
        public string Bib { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public int? Age { get; set; }
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string RegistrationStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string RaceName { get; set; } = string.Empty;
        public int? ImportBatchId { get; set; }
    }

}
