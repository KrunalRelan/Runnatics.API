namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Response model for participant details screen
    /// Contains comprehensive information about a participant's performance, rankings, and timing data
    /// </summary>
    public class ParticipantDetailsResponse
    {
        #region Basic Information

        /// <summary>
        /// Encrypted participant identifier
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Participant's BIB number
        /// </summary>
        public string? BibNumber { get; set; }

        /// <summary>
        /// Participant's first name
        /// </summary>
        public string? FirstName { get; set; }

        /// <summary>
        /// Participant's last name
        /// </summary>
        public string? LastName { get; set; }

        /// <summary>
        /// Full name (computed from first and last name)
        /// </summary>
        public string FullName => $"{FirstName} {LastName}".Trim();

        /// <summary>
        /// Initials for display (e.g., "RS" for Rahul Sharma)
        /// </summary>
        public string? Initials => $"{FirstName?.FirstOrDefault()}{LastName?.FirstOrDefault()}".ToUpper();

        /// <summary>
        /// Participant's gender
        /// </summary>
        public string? Gender { get; set; }

        /// <summary>
        /// Participant's age
        /// </summary>
        public int? Age { get; set; }

        /// <summary>
        /// Age category (e.g., "M35-39", "F30-34")
        /// </summary>
        public string? AgeCategory { get; set; }

        /// <summary>
        /// Running club or team affiliation
        /// </summary>
        public string? Club { get; set; }

        /// <summary>
        /// Current status (e.g., "Finished", "DNF", "Running")
        /// </summary>
        public string? Status { get; set; }

        #endregion

        #region Contact Information

        /// <summary>
        /// Email address
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Phone number
        /// </summary>
        public string? Phone { get; set; }

        /// <summary>
        /// Country of residence
        /// </summary>
        public string? Country { get; set; }

        #endregion

        #region Event Information

        /// <summary>
        /// Encrypted event identifier
        /// </summary>
        public string? EventId { get; set; }

        /// <summary>
        /// Name of the event (e.g., "Mumbai Marathon 2026")
        /// </summary>
        public string? EventName { get; set; }

        /// <summary>
        /// Encrypted race identifier
        /// </summary>
        public string? RaceId { get; set; }

        /// <summary>
        /// Name of the race (e.g., "Half Marathon (21.1K)")
        /// </summary>
        public string? RaceName { get; set; }

        /// <summary>
        /// Total race distance in kilometers
        /// </summary>
        public decimal? RaceDistance { get; set; }

        #endregion

        #region Timing Information

        /// <summary>
        /// Chip time (net time from crossing start line)
        /// </summary>
        public string? ChipTime { get; set; }

        /// <summary>
        /// Gun time (time from official race start)
        /// </summary>
        public string? GunTime { get; set; }

        /// <summary>
        /// Race start time
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// Time when participant crossed finish line
        /// </summary>
        public DateTime? FinishTime { get; set; }

        #endregion

        #region Performance and Analytics

        /// <summary>
        /// Performance overview including average/max speed and pace
        /// </summary>
        public PerformanceOverview? Performance { get; set; }

        /// <summary>
        /// Ranking information across different categories
        /// </summary>
        public RankingInfo? Rankings { get; set; }

        /// <summary>
        /// Checkpoint times with ranking data, ordered by distance from start.
        /// Data sourced from ReadNormalized readings and SplitTimes rankings.
        /// </summary>
        public List<CheckpointTimeInfo>? CheckpointTimes { get; set; }

        /// <summary>
        /// Split times at each checkpoint with detailed metrics
        /// </summary>
        public List<SplitTimeInfo>? SplitTimes { get; set; }

        /// <summary>
        /// Pace progression data for visualization
        /// </summary>
        public List<PaceProgressionInfo>? PaceProgression { get; set; }

        #endregion

        #region RFID Information

        /// <summary>
        /// RFID chip EPC identifier
        /// </summary>
        public string? Epc { get; set; }

        /// <summary>
        /// Raw RFID tag readings for this participant
        /// </summary>
        public List<RfidReadingDetail>? RfidReadings { get; set; }

        /// <summary>
        /// Processing notes from RFID readings
        /// </summary>
        public List<string>? ProcessingNotes { get; set; }

        #endregion
    }
}
