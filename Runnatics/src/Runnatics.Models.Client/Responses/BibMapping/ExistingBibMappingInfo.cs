namespace Runnatics.Models.Client.Responses.BibMapping
{
    public class ExistingBibMappingInfo
    {
        public string BibNumber { get; set; } = string.Empty;

        public string? ParticipantName { get; set; }

        public string ParticipantId { get; set; } = string.Empty;

        public DateTime MappedAt { get; set; }
    }
}
