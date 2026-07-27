namespace Runnatics.Services.Interface
{
    public interface IRaceNotificationService
    {
        Task NotifyCheckpointCrossingAsync(int participantId, int checkpointId, int raceId, CancellationToken ct = default);
        Task NotifyRaceCompletionAsync(int participantId, int raceId, CancellationToken ct = default);
        Task NotifyCompletionSmsAsync(int participantId, int raceId, bool force = false, CancellationToken ct = default);
        Task NotifyBibAssignedAsync(int participantId, int raceId, bool force = false, CancellationToken ct = default);
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
