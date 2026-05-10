using Runnatics.Models.Client.Notifications;

namespace Runnatics.Services.Interface
{
    public interface INotificationEmailService
    {
        Task<NotificationResult> SendCompletionEmailAsync(
            int participantId,
            int raceId,
            string email,
            string name,
            Dictionary<string, string> variables,
            CancellationToken ct = default);

        Task<NotificationResult> SendSupportTicketEmailAsync(
            string toEmail,
            string toName,
            Dictionary<string, string> variables,
            CancellationToken ct = default);
    }
}
