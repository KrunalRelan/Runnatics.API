namespace Runnatics.Services.Interface
{
    public interface IRaceNotificationService
    {
        Task NotifyCheckpointCrossingAsync(int participantId, int checkpointId, int raceId, CancellationToken ct = default);
        Task NotifyRaceCompletionAsync(int participantId, int raceId, CancellationToken ct = default);
        Task NotifyCompletionSmsAsync(int participantId, int raceId, bool force = false, CancellationToken ct = default);
        Task NotifyBibAssignedAsync(int participantId, int raceId, bool force = false, CancellationToken ct = default);

        /// <summary>
        /// Sends one completion SMS for a single participant, for verification. Composes the
        /// variables through the same production code path, but never consults or writes the
        /// "RaceCompletion" dedupe record — it logs as "RaceCompletionTest" so a test cannot
        /// suppress the participant's real results SMS. Supply <paramref name="overridePhone"/>
        /// to send to an operator's own handset instead of the participant's number.
        /// </summary>
        Task<TestCompletionSmsResult> SendCompletionSmsTestAsync(
            int participantId, int raceId, string? overridePhone = null, CancellationToken ct = default);
        Task NotifySupportTicketCreatedAsync(int queryId, CancellationToken ct = default);

        /// <summary>
        /// Emails a support-query comment to the ticket submitter. Lives here (rather than
        /// in SupportQueryService, which used to send it directly via IEmailService) so the
        /// send is written to NotificationLogs like every other outbound message.
        /// Returns true when the email was accepted for delivery.
        /// </summary>
        Task<bool> NotifySupportCommentAsync(int commentId, CancellationToken ct = default);
    }
}
