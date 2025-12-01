namespace Runnatics.Models.Client.Responses.Participants
{
    public class Participant
    {
        public int Id { get; set; }
        public string Bib { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public int? Age { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string RegistrationStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public string RaceName { get; set; }
        public int? ImportBatchId { get; set; }
    }

}
