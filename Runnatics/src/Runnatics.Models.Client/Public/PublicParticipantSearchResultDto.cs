namespace Runnatics.Models.Client.Public
{
    public class PublicParticipantSearchResultDto
    {
        public string EncryptedId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string RaceName { get; set; } = string.Empty;
        public string? ChipTime { get; set; }
    }
}
