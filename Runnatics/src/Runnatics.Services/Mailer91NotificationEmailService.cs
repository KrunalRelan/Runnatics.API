using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runnatics.Models.Client.Notifications;
using Runnatics.Services.Config;
using Runnatics.Services.Interface;
using System.Net.Http.Json;

namespace Runnatics.Services
{
    public class Mailer91NotificationEmailService(
        HttpClient httpClient,
        IOptions<Mailer91Config> options,
        ILogger<Mailer91NotificationEmailService> logger) : INotificationEmailService
    {
        private const string ApiUrl = "https://api.mailer91.com/v1/send";
        private readonly Mailer91Config _config = options.Value;

        public Task<NotificationResult> SendCompletionEmailAsync(
            int participantId, int raceId,
            string email, string name,
            Dictionary<string, string> variables,
            CancellationToken ct = default)
            => SendAsync(email, name, "RaceCompletion", variables, ct);

        public Task<NotificationResult> SendSupportTicketEmailAsync(
            string toEmail, string toName,
            Dictionary<string, string> variables,
            CancellationToken ct = default)
            => SendAsync(toEmail, toName, "SupportTicket", variables, ct);

        private async Task<NotificationResult> SendAsync(
            string toEmail, string toName, string eventType,
            Dictionary<string, string> variables, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_config.ApiKey))
                return NotificationResult.Fail("Mailer91 ApiKey not configured");

            var maskedEmail = MaskEmail(toEmail);

            try
            {
                var payload = new
                {
                    to = new[] { new { email = toEmail, name = toName } },
                    from = new { email = _config.FromEmail, name = _config.FromName },
                    subject = BuildSubject(eventType, variables),
                    body = BuildHtmlBody(eventType, variables),
                    domain = "racetik.com"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Add("Authorization", _config.ApiKey);

                var response = await httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Mailer91 returned {Status} for {Email}: {Body}",
                        (int)response.StatusCode, maskedEmail, body);
                    return NotificationResult.Fail(body);
                }

                logger.LogInformation("Email sent to {Email} ({EventType})", maskedEmail, eventType);
                return NotificationResult.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email to {Email}", maskedEmail);
                return NotificationResult.Fail(ex.Message);
            }
        }

        private static string BuildSubject(string eventType, Dictionary<string, string> vars) =>
            eventType switch
            {
                "RaceCompletion" => $"🏅 You finished {vars.GetValueOrDefault("RaceName", "the race")}!",
                "SupportTicket" => "We received your query",
                _ => "Notification from Racetik"
            };

        private static string BuildHtmlBody(string eventType, Dictionary<string, string> vars) =>
            eventType switch
            {
                "RaceCompletion" => $@"
<html><body style='font-family:Arial,sans-serif;'>
  <h2>Congratulations {vars.GetValueOrDefault("ParticipantName", "Participant")}!</h2>
  <p>You have completed <strong>{vars.GetValueOrDefault("RaceName", "")}</strong></p>
  <p>Your finish time: <strong>{vars.GetValueOrDefault("FinishTime", "")}</strong></p>
  <p>Overall rank: <strong>#{vars.GetValueOrDefault("OverallRank", "")}</strong></p>
  {(vars.TryGetValue("CertificateUrl", out var certUrl) && !string.IsNullOrEmpty(certUrl)
      ? $"<p><a href='{certUrl}' style='background:#2E75B6;color:white;padding:12px 24px;text-decoration:none;border-radius:6px;display:inline-block;'>Download Certificate</a></p>"
      : "")}
  <p>View full results at <a href='https://racetik.com'>racetik.com</a></p>
  <hr/><p style='color:#888;font-size:12px;'>Powered by Racetik</p>
</body></html>",

                "SupportTicket" => $@"
<html><body style='font-family:Arial,sans-serif;'>
  <h2>We received your query</h2>
  <p>Hi {vars.GetValueOrDefault("Name", "")},</p>
  <p>Thank you for reaching out. We've received your support request and will get back to you shortly.</p>
  <p><strong>Ticket ID:</strong> #{vars.GetValueOrDefault("TicketId", "")}</p>
  <p><strong>Your query:</strong></p>
  <p style='background:#f5f5f5;padding:12px;border-radius:6px;'>{vars.GetValueOrDefault("Query", "")}</p>
  <hr/><p style='color:#888;font-size:12px;'>Racetik Support Team</p>
</body></html>",

                _ => $"<p>{string.Join("<br/>", vars.Select(v => $"{v.Key}: {v.Value}"))}</p>"
            };

        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 1) return "***@***";
            return email[0] + new string('*', at - 1) + email[at..];
        }
    }
}
