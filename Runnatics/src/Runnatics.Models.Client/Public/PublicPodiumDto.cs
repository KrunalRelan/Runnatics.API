namespace Runnatics.Models.Client.Public
{
    public class PublicPodiumDto
    {
        public PublicPodiumEntryDto? First { get; set; }
        public PublicPodiumEntryDto? Second { get; set; }
        public PublicPodiumEntryDto? Third { get; set; }
    }

    public class PublicPodiumEntryDto
    {
        public string ParticipantId { get; set; } = string.Empty;   // encrypted
        public string Name { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FinishedTime { get; set; } = string.Empty;    // "HH:mm:ss"
        public int Rank { get; set; }
    }
}
