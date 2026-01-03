namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Performance overview metrics for a participant
    /// </summary>
    public class PerformanceOverview
    {
        /// <summary>
        /// Average speed in kilometers per hour
        /// </summary>
        public decimal? AverageSpeed { get; set; }

        /// <summary>
        /// Average pace in min/km format (e.g., "5:20/km")
        /// </summary>
        public string? AveragePace { get; set; }

        /// <summary>
        /// Maximum speed achieved in kilometers per hour
        /// </summary>
        public decimal? MaxSpeed { get; set; }

        /// <summary>
        /// Best pace achieved in min/km format (e.g., "4:45/km")
        /// </summary>
        public string? BestPace { get; set; }
    }
}
