namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Split time information for a specific checkpoint
    /// </summary>
    public class SplitTimeInfo
    {
        /// <summary>
        /// Encrypted checkpoint identifier
        /// </summary>
        public string? CheckpointId { get; set; }

        /// <summary>
        /// Name of the checkpoint (e.g., "5K", "10K", "Finish")
        /// </summary>
        public string? CheckpointName { get; set; }

        /// <summary>
        /// Distance formatted as string (e.g., "5 km", "10 km")
        /// </summary>
        public string? Distance { get; set; }

        /// <summary>
        /// Distance in kilometers as numeric value
        /// </summary>
        public decimal? DistanceKm { get; set; }

        /// <summary>
        /// Time taken for this segment (e.g., "00:25:30")
        /// </summary>
        public string? SplitTime { get; set; }

        /// <summary>
        /// Total time from start to this checkpoint (e.g., "00:51:30")
        /// </summary>
        public string? CumulativeTime { get; set; }

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
        /// Overall rank at this checkpoint
        /// </summary>
        public int? OverallRank { get; set; }

        /// <summary>
        /// Gender-based rank at this checkpoint
        /// </summary>
        public int? GenderRank { get; set; }

        /// <summary>
        /// Category-based rank at this checkpoint
        /// </summary>
        public int? CategoryRank { get; set; }

        /// <summary>
        /// Whether this checkpoint reading was manually entered
        /// </summary>
        public bool IsManual { get; set; }
    }
}
