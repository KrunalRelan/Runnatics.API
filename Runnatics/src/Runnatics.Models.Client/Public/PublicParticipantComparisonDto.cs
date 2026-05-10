namespace Runnatics.Models.Client.Public
{
    public class PublicParticipantComparisonDto
    {
        public PublicComparisonParticipantDto Participant1 { get; set; } = new();
        public PublicComparisonParticipantDto Participant2 { get; set; } = new();
        public List<PublicComparisonDiffDto> Differences { get; set; } = [];
    }

    public class PublicComparisonParticipantDto
    {
        public string Name { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string? ChipTime { get; set; }
        public string? GunTime { get; set; }
        public string? Pace { get; set; }
        public List<PublicComparisonSplitDto> Splits { get; set; } = [];
    }

    public class PublicComparisonSplitDto
    {
        public string Checkpoint { get; set; } = string.Empty;
        public string? Time { get; set; }
        public string? Pace { get; set; }
    }

    public class PublicComparisonDiffDto
    {
        public string Checkpoint { get; set; } = string.Empty;
        public string TimeDiff { get; set; } = string.Empty;   // "+00:01:23" or "-00:00:45"
        public int Faster { get; set; }                        // 1 or 2
    }
}
