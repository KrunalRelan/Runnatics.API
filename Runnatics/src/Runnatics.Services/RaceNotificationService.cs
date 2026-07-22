using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Notifications;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System.Globalization;

namespace Runnatics.Services
{
    public class RaceNotificationService(
        IUnitOfWork<RaceSyncDbContext> unitOfWork,
        INotificationSmsService smsService,
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
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

            // Guard: skip if already sent within the RFID dedup window (30s)
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

            var race = await unitOfWork.GetRepository<Race>()
                .GetQuery(r => r.Id == raceId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            // Provisional elapsed time at this checkpoint (cumulative ms from race start).
            var split = await unitOfWork.GetRepository<SplitTimes>()
                .GetQuery(s => s.ParticipantId == participantId && s.ToCheckpointId == checkpointId)
                .AsNoTracking()
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);

            // MSG91 template 6a4cc0c9 is positional: var1=name, var2=provisional time, var3=race.
            var variables = new Dictionary<string, string>
            {
                ["var1"] = $"{participant.FirstName} {participant.LastName}".Trim(),
                ["var2"] = FormatMs(split?.SplitTimeMs),
                ["var3"] = race?.Title ?? string.Empty
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

            var participantName = $"{participant.FirstName} {participant.LastName}".Trim();
            var raceName = raceResult.Race?.Title ?? string.Empty;
            var finishTime = FormatMs(raceResult.FinishTime);
            var rank = raceResult.OverallRank ?? 0;

            // SMS auto-send is gated per-event (default OFF). Email is NOT gated — it keeps its
            // current auto-send behavior. The manual "Send Results SMS" path bypasses this gate.
            var autoSendSms = await unitOfWork.GetRepository<EventSettings>()
                .GetQuery(s => s.EventId == participant.EventId)
                .AsNoTracking()
                .Select(s => (bool?)s.AutoSendCompletionSms)
                .FirstOrDefaultAsync(ct) ?? false;

            if (autoSendSms)
                await SendCompletionSmsCoreAsync(participant, raceId, raceResult, force: false, ct);

            // Email via Hostinger SMTP (unchanged)
            if (!string.IsNullOrWhiteSpace(participant.Email))
            {
                var htmlBody = emailTemplateService.BuildRaceResultNotification(
                    participantName, raceName, finishTime, rank, "Finished");
                var sent = await emailService.SendAsync(
                    participant.Email,
                    $"Your results for {raceName} are ready!",
                    htmlBody);
                var emailResult = sent ? NotificationResult.Ok() : NotificationResult.Fail("SMTP send failed");
                await LogAsync("Email", "RaceCompletion", participantId, raceId, participant.Email, emailResult, ct);
            }
        }

        // Manual "Send Results SMS" path — SMS only (no email), bypasses the per-event auto toggle,
        // but still dedupes so clicking twice / re-running won't double-send. Used by the queue.
        public async Task NotifyCompletionSmsAsync(
            int participantId, int raceId, bool force = false,
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

            await SendCompletionSmsCoreAsync(participant, raceId, raceResult, force, ct);
        }

        // Shared completion-SMS send: phone gate + dedupe (unless force) + positional var1/var2/var3
        // + log. Used by both the auto (finish-triggered) and manual (bulk) paths.
        private async Task SendCompletionSmsCoreAsync(
            Participant participant, int raceId, Results raceResult, bool force, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(participant.Phone)) return;

            if (!force)
            {
                var alreadySent = await unitOfWork.GetRepository<NotificationLog>()
                    .GetQuery()
                    .AnyAsync(n =>
                        n.Channel == "SMS" &&
                        n.EventType == "RaceCompletion" &&
                        n.ParticipantId == participant.Id &&
                        n.RaceId == raceId &&
                        n.Success, ct);

                if (alreadySent) return;
            }

            // MSG91 template 69e08448 is positional: var1=name, var2=time, var3=race.
            var smsVars = new Dictionary<string, string>
            {
                ["var1"] = $"{participant.FirstName} {participant.LastName}".Trim(),
                ["var2"] = FormatMs(raceResult.FinishTime),
                ["var3"] = raceResult.Race?.Title ?? string.Empty
            };

            var smsResult = await smsService.SendCompletionSmsAsync(participant.Id, raceId, participant.Phone, smsVars, ct);
            await LogAsync("SMS", "RaceCompletion", participant.Id, raceId, participant.Phone, smsResult, ct);
        }

        public async Task NotifyBibAssignedAsync(
            int participantId, int raceId, bool force = false,
            CancellationToken ct = default)
        {
            var participant = await LoadParticipantAsync(participantId, ct);
            if (participant == null) return;

            var phone = participant.Phone;
            if (string.IsNullOrWhiteSpace(phone)) return;

            // Dedup: a participant is notified about their BIB once per race. An edit that
            // CHANGED the bib passes force=true to intentionally re-notify.
            if (!force)
            {
                var alreadySent = await unitOfWork.GetRepository<NotificationLog>()
                    .GetQuery()
                    .AnyAsync(n =>
                        n.Channel == "SMS" &&
                        n.EventType == "BibAssigned" &&
                        n.ParticipantId == participantId &&
                        n.RaceId == raceId &&
                        n.Success, ct);

                if (alreadySent) return;
            }

            var evt = await unitOfWork.GetRepository<Event>()
                .GetQuery(e => e.Id == participant.EventId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            var race = await unitOfWork.GetRepository<Race>()
                .GetQuery(r => r.Id == raceId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            // MSG91 template 6a5b4ff is positional: var1=name, var2=bib, var3=race, var4=date, var5=venue.
            var variables = new Dictionary<string, string>
            {
                ["var1"] = $"{participant.FirstName} {participant.LastName}".Trim(),
                ["var2"] = participant.BibNumber ?? string.Empty,
                ["var3"] = race?.Title ?? evt?.Name ?? string.Empty,
                ["var4"] = FormatEventLocalDate(evt?.EventDate, evt?.TimeZone),
                // Never empty — the template renders "at {var5}", so a blank would dangle.
                ["var5"] = FirstNonBlank(evt?.VenueName, evt?.City, evt?.Name) ?? "-"
            };

            var result = await smsService.SendBibAssignedSmsAsync(participantId, raceId, phone, variables, ct);
            await LogAsync("SMS", "BibAssigned", participantId, raceId, phone, result, ct);
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

            var htmlBody = emailTemplateService.BuildSupportQueryConfirmation(
                submitterName: email,
                subject: query.Subject,
                ticketId: query.Id.ToString());

            var sent = await emailService.SendAsync(email, "We received your support query", htmlBody);
            var result = sent ? NotificationResult.Ok() : NotificationResult.Fail("SMTP send failed");
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
            NotificationResult result,
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

        // EventDate is stored UTC; render it in the event's timezone (default Asia/Kolkata)
        // so the participant sees the correct calendar day in IST, e.g. "18 Jul 2026".
        private static string FormatEventLocalDate(DateTime? utc, string? timeZoneId)
        {
            if (!utc.HasValue) return string.Empty;

            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(
                    string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Kolkata" : timeZoneId);
            }
            catch
            {
                try { tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
                catch { tz = TimeZoneInfo.Utc; }
            }

            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), tz);
            return local.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        private static string? FirstNonBlank(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}
