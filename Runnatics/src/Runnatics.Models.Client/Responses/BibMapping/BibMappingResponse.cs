namespace Runnatics.Models.Client.Responses.BibMapping
{
    public class BibMappingResponse
    {
        public string Id { get; set; } = string.Empty;           // Encrypted ChipAssignment composite key as a surrogate
        public string ChipId { get; set; } = string.Empty;       // Encrypted
        public string ParticipantId { get; set; } = string.Empty; // Encrypted
        public string RaceId { get; set; } = string.Empty;       // Encrypted
        public string EventId { get; set; } = string.Empty;      // Encrypted
        public string BibNumber { get; set; } = string.Empty;
        public string Epc { get; set; } = string.Empty;
        public string? ParticipantName { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
