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

        public Task<NotificationResult> SendBibAssignedSmsAsync(
            int participantId, int raceId, string phone,
            Dictionary<string, string> variables, CancellationToken ct = default)
            => SendAsync(_config.BibAssignedTemplateId, phone, variables, ct);

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

                var parsed = ParseFlowResponse(body);

                // Flow signals rejection in the payload, not the status code — a rejected send
                // arrives as HTTP 200 with type="error". Treating that as success is how a
                // failure becomes an indistinguishable Success=true row in NotificationLogs.
                if (!parsed.Accepted)
                {
                    logger.LogWarning(
                        "MSG91 rejected the send for {Phone} on template {TemplateId} (HTTP 200, type={Type}): {Body}",
                        maskedPhone, templateId, parsed.Type ?? "(none)", body);
                    return NotificationResult.Fail(parsed.Error ?? body);
                }

                if (parsed.RequestId is null)
                {
                    logger.LogWarning(
                        "MSG91 accepted the send for {Phone} but no request id was found — "
                        + "NotificationLogs.ProviderMessageId will be null and the message cannot be "
                        + "correlated to the MSG91 dashboard. Body: {Body}",
                        maskedPhone, body);
                }

                logger.LogInformation("SMS sent to {Phone} via template {TemplateId}, request {RequestId}",
                    maskedPhone, templateId, parsed.RequestId ?? "(none)");
                return NotificationResult.Ok(parsed.RequestId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send SMS to {Phone}", maskedPhone);
                return NotificationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Outcome of an MSG91 Flow v5 response body.
        /// </summary>
        /// <param name="Accepted">False only when the payload explicitly says type="error".</param>
        /// <param name="RequestId">The provider's id for the send, or null if none was present.</param>
        /// <param name="Type">The raw "type" field, for logging.</param>
        /// <param name="Error">Error text when rejected.</param>
        public readonly record struct Msg91FlowResponse(
            bool Accepted, string? RequestId, string? Type, string? Error);

        /// <summary>
        /// Reads an MSG91 Flow v5 response body.
        ///
        /// Flow returns <c>{"message":"&lt;request id&gt;","type":"success"}</c> on acceptance and
        /// <c>{"message":"&lt;error text&gt;","type":"error"}</c> on rejection — so "message" is the
        /// request id ONLY when the send was accepted. "request_id" is the older v2 sendsms key and
        /// is preferred when present, since a body carrying it is unambiguous.
        ///
        /// An unparseable or unrecognised body is treated as ACCEPTED with no request id: the
        /// provider returned 2xx, so the message may well have gone out, and failing it here would
        /// invite a duplicate send on the next attempt. The caller logs the body in that case.
        /// </summary>
        public static Msg91FlowResponse ParseFlowResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return new Msg91FlowResponse(true, null, null, null);

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(body);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return new Msg91FlowResponse(true, null, null, null);
            }

            if (root.ValueKind != JsonValueKind.Object)
                return new Msg91FlowResponse(true, null, null, null);

            var type = root.TryGetProperty("type", out var typeProp)
                    && typeProp.ValueKind == JsonValueKind.String
                ? typeProp.GetString()
                : null;

            var isError = string.Equals(type, "error", StringComparison.OrdinalIgnoreCase);

            var message = root.TryGetProperty("message", out var msgProp)
                ? (msgProp.ValueKind == JsonValueKind.String ? msgProp.GetString() : msgProp.ToString())
                : null;

            if (isError)
                return new Msg91FlowResponse(false, null, type, string.IsNullOrWhiteSpace(message) ? body : message);

            // Accepted: prefer an explicit request_id, else fall back to "message".
            var requestId =
                root.TryGetProperty("request_id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : message;

            return new Msg91FlowResponse(true, string.IsNullOrWhiteSpace(requestId) ? null : requestId, type, null);
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
