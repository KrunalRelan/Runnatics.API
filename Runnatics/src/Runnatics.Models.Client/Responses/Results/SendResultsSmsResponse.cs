namespace Runnatics.Models.Client.Responses.Results
{
    /// <summary>Result of the manual "Send Results SMS" bulk action.</summary>
    public class SendResultsSmsResponse
    {
        /// <summary>Finished participants found for the race.</summary>
        public int FinishedCount { get; set; }

        /// <summary>Jobs successfully queued (a full queue or missing phone reduces this vs FinishedCount).</summary>
        public int QueuedCount { get; set; }

        /// <summary>Finishers skipped because the queue was full.</summary>
        public int SkippedCount { get; set; }
    }
}
