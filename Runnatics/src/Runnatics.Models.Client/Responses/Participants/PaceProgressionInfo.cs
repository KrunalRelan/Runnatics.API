namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Pace progression information for visualization
    /// </summary>
    public class PaceProgressionInfo
    {
        /// <summary>
        /// Segment identifier (e.g., "5K", "10K", "FINISH")
        /// </summary>
        public string? Segment { get; set; }

        /// <summary>
        /// Pace for this segment formatted as min/km (e.g., "5:06/km")
        /// </summary>
        public string? Pace { get; set; }

        /// <summary>
        /// Numeric pace value in minutes per kilometer
        /// </summary>
        public decimal? PaceValue { get; set; }

        /// <summary>
        /// Speed for this segment in kilometers per hour
        /// </summary>
        public decimal? Speed { get; set; }

        /// <summary>
        /// Time taken for this segment (e.g., "00:25:30")
        /// </summary>
        public string? SplitTime { get; set; }

        /// <summary>
        /// Distance from start in kilometers
        /// </summary>
        public decimal? DistanceKm { get; set; }

        /// <summary>
        /// Direction of pace change compared to previous segment
        /// Values: "improved" (faster), "declined" (slower), "first" (first split), "none" (start or no data)
        /// </summary>
        public string? PaceChangeDirection { get; set; }

        /// <summary>
        /// Percentage change in pace compared to previous segment
        /// Negative = improved (faster), Positive = declined (slower)
        /// </summary>
        public decimal? PaceChangePercent { get; set; }
    }
}
