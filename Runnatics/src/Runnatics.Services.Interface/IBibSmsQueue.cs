namespace Runnatics.Services.Interface
{
    /// <summary>A queued request to send a "BIB assigned" SMS for one participant.</summary>
    public readonly record struct BibSmsJob(int ParticipantId, int RaceId);

    /// <summary>
    /// In-process background queue for bulk BIB-assigned SMS (used by CSV import so a large
    /// import returns fast instead of firing hundreds of synchronous SMS). Add/edit send inline.
    /// </summary>
    public interface IBibSmsQueue
    {
        /// <summary>Enqueue a job. Returns false if the queue is full (caller should log the skip).</summary>
        bool TryEnqueue(BibSmsJob job);

        /// <summary>Drains jobs for the background dispatcher.</summary>
        IAsyncEnumerable<BibSmsJob> DequeueAllAsync(CancellationToken ct);
    }
}
