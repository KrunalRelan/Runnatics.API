using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class RaceNotificationService(
        IUnitOfWork<RaceSyncDbContext> unitOfWork,
        INotificationSmsService smsService,
        INotificationEmailService emailService,
        ILogger<RaceNotificationService> logger) : IRaceNotificationService
    {
        public async Task NotifyCheckpointCrossingAsync(
            int participantId, int checkpointId, int raceId,
            CancellationToken ct = default)
        {
            var participant = await LoadParticipantAsync(participantId, ct);
            if (participant == null) return;

            var phone = participant.Phone;
            if (string.IsNullOrWhiteSpace(phone)) return;

            // Guard: skip if a notification was already sent within the dedup window (30s)
            var recentCutoff = DateTime.UtcNow.AddSeconds(-30);
            var alreadySent = await unitOfWork.GetRepository<NotificationLog>()
                .GetQuery()
                .AnyAsync(n =>
                    n.Channel == "SMS" &&
                    n.EventType == "CheckpointCrossing" &&
                    n.ParticipantId == participantId &&
                    n.RaceId == raceId &&
                    n.Success &&
                    n.SentAt >= recentCutoff, ct);

            if (alreadySent) return;

            var checkpoint = await unitOfWork.GetRepository<Checkpoint>()
                .GetQuery(c => c.Id == checkpointId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            var variables = new Dictionary<string, string>
            {
                ["name1"] = $"{participant.FirstName} {participant.LastName}".Trim(),
                ["event"] = checkpoint?.Name ?? "Checkpoint"
            };

            var result = await smsService.SendCheckpointSmsAsync(participantId, raceId, phone, variables, ct);
            await LogAsync("SMS", "CheckpointCrossing", participantId, raceId, phone, result, ct);
        }

        public async Task NotifyRaceCompletionAsync(
            int participantId, int raceId,
            CancellationToken ct = default)
        {
            var participant = await LoadParticipantAsync(participantId, ct);
            if (participant == null) return;

            var raceResult = await unitOfWork.GetRepository<Results>()
                .GetQuery(r => r.ParticipantId == participantId && r.RaceId == raceId)
                .AsNoTracking()
                .Include(r => r.Race)
                .FirstOrDefaultAsync(ct);

            if (raceResult == null) return;

            var raceName = raceResult.Race?.Title ?? string.Empty;
            var finishTime = FormatMs(raceResult.FinishTime);
            var rank = raceResult.OverallRank?.ToString() ?? string.Empty;

            var variables = new Dictionary<string, string>
            {
                ["name1"] = $"{participant.FirstName} {participant.LastName}".Trim(),
                ["time"] = finishTime,
                ["event"] = raceName,
                ["ParticipantName"] = $"{participant.FirstName} {participant.LastName}".Trim(),
                ["RaceName"] = raceName,
                ["FinishTime"] = finishTime,
                ["OverallRank"] = rank
            };

            if (!string.IsNullOrWhiteSpace(participant.Phone))
            {
                var smsResult = await smsService.SendCompletionSmsAsync(participantId, raceId, participant.Phone, variables, ct);
                await LogAsync("SMS", "RaceCompletion", participantId, raceId, participant.Phone, smsResult, ct);
            }

            if (!string.IsNullOrWhiteSpace(participant.Email))
            {
                var name = $"{participant.FirstName} {participant.LastName}".Trim();
                var emailResult = await emailService.SendCompletionEmailAsync(participantId, raceId, participant.Email, name, variables, ct);
                await LogAsync("Email", "RaceCompletion", participantId, raceId, participant.Email, emailResult, ct);
            }
        }

        public async Task NotifySupportTicketCreatedAsync(int queryId, CancellationToken ct = default)
        {
            var query = await unitOfWork.GetRepository<SupportQuery>()
                .GetQuery(q => q.Id == queryId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (query == null) return;

            var email = query.SubmitterEmail;
            if (string.IsNullOrWhiteSpace(email)) return;

            var variables = new Dictionary<string, string>
            {
                ["Name"] = email,
                ["TicketId"] = query.Id.ToString(),
                ["Query"] = query.Subject
            };

            var result = await emailService.SendSupportTicketEmailAsync(email, email, variables, ct);
            await LogAsync("Email", "SupportTicket", null, null, email, result, ct);
        }

        private async Task<Participant?> LoadParticipantAsync(int participantId, CancellationToken ct)
        {
            var participant = await unitOfWork.GetRepository<Participant>()
                .GetQuery(p =>
                    p.Id == participantId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (participant == null)
                logger.LogWarning("RaceNotificationService: participant {Id} not found", participantId);

            return participant;
        }

        private async Task LogAsync(
            string channel, string eventType,
            int? participantId, int? raceId,
            string recipient,
            Models.Client.Notifications.NotificationResult result,
            CancellationToken ct)
        {
            try
            {
                var log = new NotificationLog
                {
                    Channel = channel,
                    EventType = eventType,
                    ParticipantId = participantId,
                    RaceId = raceId,
                    Recipient = recipient,
                    Success = result.Success,
                    ProviderMessageId = result.ProviderMessageId,
                    ErrorMessage = result.ErrorMessage,
                    SentAt = DateTime.UtcNow
                };

                await unitOfWork.GetRepository<NotificationLog>().AddAsync(log);
                await unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to log notification ({Channel}/{EventType})", channel, eventType);
            }
        }

        private static string FormatMs(long? ms)
        {
            if (!ms.HasValue || ms.Value <= 0) return "--:--:--";
            return TimeSpan.FromMilliseconds(ms.Value).ToString(@"hh\:mm\:ss");
        }
    }
}
