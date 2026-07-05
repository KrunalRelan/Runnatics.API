namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Response for race results calculation
    /// </summary>
    public class CalculateResultsResponse
    {
        public DateTime ProcessedAt { get; set; }
        public string Status { get; set; } = "Processing";
        public string? Message { get; set; }

        /// <summary>
        /// Total number of participants who finished the race
        /// </summary>
        public int TotalFinishers { get; set; }

        /// <summary>
        /// Number of new results created
        /// </summary>
        public int ResultsCreated { get; set; }

        /// <summary>
        /// Number of existing results updated
        /// </summary>
        public int ResultsUpdated { get; set; }

        /// <summary>
        /// Number of participants who did not finish (no finish line reading)
        /// </summary>
        public int DNFCount { get; set; }

        /// <summary>
        /// Processing time in milliseconds
        /// </summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// Breakdown by gender
        /// </summary>
        public GenderBreakdown GenderStats { get; set; } = new GenderBreakdown();

        /// <summary>
        /// Number of unique categories processed
        /// </summary>
        public int CategoriesProcessed { get; set; }

        public int DNSCount { get; set; }

        /// <summary>
        /// FINISH CEILING (Races.EndTime) aggregate flag: set when runners were DNF'd solely
        /// because their only finish-gate read(s) fell after Race.EndTime — includes the count
        /// and the nearest-miss delta, so a WRONG EndTime announces itself on every reprocess.
        /// Null when the ceiling flagged nobody (or the feature is off).
        /// </summary>
        public string? FinishCeilingNote { get; set; }
    }
}

