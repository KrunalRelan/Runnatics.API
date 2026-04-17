namespace Runnatics.Models.Client.Public
{
    public class PublicResultDto
    {
        // Maps from Results.Participant.BibNumber
        public string BibNumber { get; set; } = string.Empty;

        // Maps from Results.Participant.FullName (FirstName + LastName)
        public string ParticipantName { get; set; } = string.Empty;

        // Maps from Results.Race.Title
        public string RaceName { get; set; } = string.Empty;

        // Maps from Results.Participant.AgeCategory
        public string? AgeGroup { get; set; }

        // Maps from Results.Participant.Gender
        public string? Gender { get; set; }

        // Maps from Results.GunTime (ms) converted to TimeSpan
        public TimeSpan? GunTime { get; set; }

        // Maps from Results.NetTime (ms) converted to TimeSpan
        public TimeSpan? NetTime { get; set; }

        public int? OverallRank { get; set; }

        public int? CategoryRank { get; set; }

        public int? GenderRank { get; set; }

        public List<PublicSplitDto>? Splits { get; set; }
    }
}
