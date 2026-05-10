using Runnatics.Models.Client.Notifications;

namespace Runnatics.Services.Interface
{
    public interface INotificationSmsService
    {
        Task<NotificationResult> SendCheckpointSmsAsync(
            int participantId,
            int raceId,
            string phone,
            Dictionary<string, string> variables,
            CancellationToken ct = default);

        Task<NotificationResult> SendCompletionSmsAsync(
            int participantId,
            int raceId,
            string phone,
            Dictionary<string, string> variables,
            CancellationToken ct = default);
    }
}
