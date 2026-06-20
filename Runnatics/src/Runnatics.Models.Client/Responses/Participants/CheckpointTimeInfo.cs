namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Checkpoint time with ranking data for a participant.
    /// Data sourced from ReadNormalized and SplitTimes tables.
    /// </summary>
    public class CheckpointTimeInfo
    {
        /// <summary>
        /// Encrypted checkpoint id — lets the UI record a manual time at this checkpoint
        /// even when the participant has no detection/split here yet.
        /// </summary>
        public string? CheckpointId { get; set; }

        /// <summary>
        /// Checkpoint name (e.g., "Start", "5 Km", "Finish")
        /// </summary>
        public string? CheckpointName { get; set; }

        /// <summary>
        /// Distance from start in kilometers
        /// </summary>
        public decimal? DistanceKm { get; set; }

        /// <summary>
        /// Chip time converted to event's local timezone (HH:mm:ss), or null if not crossed
        /// </summary>
        public string? Time { get; set; }

        /// <summary>
        /// Full crossing date+time in the event's local timezone ("yyyy-MM-ddTHH:mm:ss"), or null
        /// if not crossed. The manual-time editor pre-fills DATE and time from this so a near-midnight
        /// gun (crossings on the next calendar day) defaults to the actual crossing date, not the
        /// race start date.
        /// </summary>
        public string? LocalDateTime { get; set; }

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
    }
}
