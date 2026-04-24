namespace Runnatics.Models.Client.Responses.Participants
{
    public class ParticipantSearchReponse
    {
        public string? Id { get; set; }
        public string? Bib { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public bool? CheckedIn { get; set; }
        public string? ChipId { get; set; }

        /// <summary>
        /// True if the participant has an active EPC chip assignment
        /// </summary>
        public bool IsEpcMapped { get; set; }

        /// <summary>
        /// Dictionary of checkpoint times keyed by checkpoint name (e.g., "Start", "5 KM", "Finish")
        /// Values are formatted times (HH:mm:ss) or null if not crossed
        /// </summary>
        public Dictionary<string, string?>? CheckpointTimes { get; set; }

        /// <summary>
        /// Ordered list of checkpoint crossing times with structured metadata
        /// </summary>
        public List<CheckpointTimeDto>? Checkpoints { get; set; }

        // Results data - populated from Results table
        /// <summary>
        /// Gun Time - total race time from gun start (formatted as HH:mm:ss or mm:ss)
        /// </summary>
        public string? GunTime { get; set; }

        /// <summary>
        /// Net/Chip Time - time from participant crossing start line (formatted as HH:mm:ss or mm:ss)
        /// </summary>
        public string? NetTime { get; set; }

        /// <summary>
        /// Overall rank among all finishers
        /// </summary>
        public int? OverallRank { get; set; }

        /// <summary>
        /// Rank within gender category
        /// </summary>
        public int? GenderRank { get; set; }

        /// <summary>
        /// Rank within age category
        /// </summary>
        public int? CategoryRank { get; set; }
    }
}
