namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Response model for updating existing participants by bib number from CSV
    /// </summary>
    public class UpdateParticipantsByBibResponse
    {
        /// <summary>
        /// Number of participants successfully updated
        /// </summary>
        public int TotalUpdated { get; set; }

        /// <summary>
        /// Number of bib numbers from CSV that were not found in existing participants
        /// </summary>
        public int TotalNotFound { get; set; }

        /// <summary>
        /// Number of rows skipped due to errors or missing bib numbers
        /// </summary>
        public int TotalSkipped { get; set; }

        /// <summary>
        /// List of bib numbers from CSV that were not found in existing participants
        /// </summary>
        public List<string> NotFoundBibNumbers { get; set; } = [];

        /// <summary>
        /// List of validation errors encountered during processing
        /// </summary>
        public List<ValidationError> Errors { get; set; } = [];

        /// <summary>
        /// Processing status: Processing, Completed, CompletedWithErrors, Failed
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Name of the uploaded file
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the update was processed
        /// </summary>
        public DateTime ProcessedAt { get; set; }
    }
}
