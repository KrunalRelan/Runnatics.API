namespace Runnatics.Services.Interface
{
    /// <summary>A queued request to send a completion ("Results") SMS for one finished participant.</summary>
    public readonly record struct CompletionSmsJob(int ParticipantId, int RaceId);

    /// <summary>
    /// In-process background queue for the manual "Send Results SMS" bulk action, so a race with
    /// hundreds of finishers returns fast instead of sending synchronously. Dedupe runs per job.
    /// </summary>
    public interface ICompletionSmsQueue
    {
        /// <summary>Enqueue a job. Returns false if the queue is full (caller should log the skip).</summary>
        bool TryEnqueue(CompletionSmsJob job);

        /// <summary>Drains jobs for the background dispatcher.</summary>
        IAsyncEnumerable<CompletionSmsJob> DequeueAllAsync(CancellationToken ct);
    }
}
