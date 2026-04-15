namespace Runnatics.Models.Client.Responses.Participants
{
    public class CheckpointTimeDto
    {
        public string CheckpointName { get; set; } = string.Empty;

        /// <summary>
        /// 1-based order of the checkpoint by distance from start
        /// </summary>
        public int CheckpointOrder { get; set; }

        /// <summary>
        /// Crossing time in event local timezone, formatted as HH:mm:ss, or null if not crossed
        /// </summary>
        public string? Time { get; set; }
    }
}
