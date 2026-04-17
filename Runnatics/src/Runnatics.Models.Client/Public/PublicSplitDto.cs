namespace Runnatics.Models.Client.Public
{
    public class PublicSplitDto
    {
        // Maps from SplitTimes.ToCheckpoint.Name
        public string CheckpointName { get; set; } = string.Empty;

        // Maps from SplitTimes.SplitTimeMs (cumulative) converted to TimeSpan
        public TimeSpan? Time { get; set; }

        // Maps from SplitTimes.Rank
        public int? Rank { get; set; }
    }
}
