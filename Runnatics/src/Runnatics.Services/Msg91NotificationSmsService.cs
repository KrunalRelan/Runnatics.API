using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runnatics.Models.Client.Notifications;
using Runnatics.Services.Config;
using Runnatics.Services.Interface;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runnatics.Services
{
    public class Msg91NotificationSmsService(
        HttpClient httpClient,
        IOptions<Msg91Config> options,
        ILogger<Msg91NotificationSmsService> logger) : INotificationSmsService
    {
        private const string FlowApiUrl = "https://control.msg91.com/api/v5/flow";
        private readonly Msg91Config _config = options.Value;

        public Task<NotificationResult> SendCheckpointSmsAsync(
            int participantId, int raceId, string phone,
            Dictionary<string, string> variables, CancellationToken ct = default)
            => SendAsync(_config.CheckpointTemplateId, phone, variables, ct);

        public Task<NotificationResult> SendCompletionSmsAsync(
            int participantId, int raceId, string phone,
            Dictionary<string, string> variables, CancellationToken ct = default)
            => SendAsync(_config.CompletionTemplateId, phone, variables, ct);

        private async Task<NotificationResult> SendAsync(
            string templateId, string phone,
            Dictionary<string, string> variables, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_config.AuthKey))
                return NotificationResult.Fail("MSG91 AuthKey not configured");

            if (string.IsNullOrEmpty(templateId))
                return NotificationResult.Fail("MSG91 template ID not configured");

            var maskedPhone = MaskPhone(phone);

            try
            {
                var recipient = new Dictionary<string, string>(variables)
                {
                    ["mobiles"] = FormatPhone(phone)
                };

                var payload = new
                {
                    template_id = templateId,
                    short_url = "0",
                    recipients = new[] { recipient }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, FlowApiUrl)
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Add("authkey", _config.AuthKey);

                var response = await httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("MSG91 returned {Status} for {Phone}: {Body}",
                        (int)response.StatusCode, maskedPhone, body);
                    return NotificationResult.Fail(body);
                }

                string? requestId = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    doc.RootElement.TryGetProperty("request_id", out var idProp);
                    requestId = idProp.GetString();
                }
                catch { }

                logger.LogInformation("SMS sent to {Phone} via template {TemplateId}", maskedPhone, templateId);
                return NotificationResult.Ok(requestId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send SMS to {Phone}", maskedPhone);
                return NotificationResult.Fail(ex.Message);
            }
        }

        private static string FormatPhone(string phone)
        {
            phone = phone.Trim().Replace(" ", "").Replace("-", "");
            if (phone.StartsWith("+"))
                phone = phone.TrimStart('+');
            if (phone.Length == 10)
                phone = $"91{phone}";
            return phone;
        }

        private static string MaskPhone(string phone)
        {
            if (phone.Length <= 4) return "****";
            return new string('*', phone.Length - 4) + phone[^4..];
        }
    }
}
