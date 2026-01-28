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
    }
}

