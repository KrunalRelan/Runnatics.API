namespace Runnatics.Models.Client.Public
{
    public class PublicParticipantInfoDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Bib { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? Distance { get; set; }
    }
}
