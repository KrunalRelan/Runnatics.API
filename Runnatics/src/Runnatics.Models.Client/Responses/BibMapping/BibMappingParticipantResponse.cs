namespace Runnatics.Models.Client.Responses.BibMapping
{
    public class BibMappingParticipantResponse
    {
        public string ParticipantId { get; set; } = string.Empty; // Encrypted
        public string? BibNumber { get; set; }
        public string? Name { get; set; }
        public bool IsEpcMapped { get; set; }
        public string? Epc { get; set; }
        public DateTime? MappedAt { get; set; }
        public string? ChipId { get; set; } // Encrypted; null when unmapped
        public string? EventId { get; set; } // Encrypted; null when unmapped
    }
}
