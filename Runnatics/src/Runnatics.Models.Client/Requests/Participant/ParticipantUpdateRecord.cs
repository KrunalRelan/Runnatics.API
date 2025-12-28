namespace Runnatics.Models.Client.Requests.Participant
{
    /// <summary>
    /// Represents a single row from CSV file for updating participant data by bib number
    /// </summary>
    public class ParticipantUpdateRecord
    {
        public int RowNumber { get; set; }
        public string? BibNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }
        public string? AgeCategory { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? TShirtSize { get; set; }
        public DateTime? DateOfBirth { get; set; }

        /// <summary>
        /// Checks if any updateable field has a value
        /// </summary>
        public bool HasAnyData =>
            !string.IsNullOrWhiteSpace(FirstName) ||
            !string.IsNullOrWhiteSpace(LastName) ||
            !string.IsNullOrWhiteSpace(Email) ||
            !string.IsNullOrWhiteSpace(Phone) ||
            !string.IsNullOrWhiteSpace(Gender) ||
            !string.IsNullOrWhiteSpace(AgeCategory) ||
            !string.IsNullOrWhiteSpace(Country) ||
            !string.IsNullOrWhiteSpace(City) ||
            !string.IsNullOrWhiteSpace(TShirtSize) ||
            DateOfBirth.HasValue;
    }
}
