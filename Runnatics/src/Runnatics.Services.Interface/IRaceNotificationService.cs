namespace Runnatics.Services.Interface
{
    public interface IRaceNotificationService
    {
        Task NotifyCheckpointCrossingAsync(int participantId, int checkpointId, int raceId, CancellationToken ct = default);
        Task NotifyRaceCompletionAsync(int participantId, int raceId, CancellationToken ct = default);
        Task NotifySupportTicketCreatedAsync(int queryId, CancellationToken ct = default);
    }
}
