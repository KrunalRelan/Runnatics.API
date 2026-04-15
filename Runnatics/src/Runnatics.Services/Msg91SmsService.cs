using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Services.Interface;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runnatics.Services
{
    public class Msg91SmsService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<Msg91SmsService> logger) : ISmsService
    {
        private const string FlowApiUrl = "https://control.msg91.com/api/v5/flow/";

        private readonly HttpClient _httpClient = httpClient;
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<Msg91SmsService> _logger = logger;

        public async Task<bool> SendSmsAsync(string phoneNumber, string message)
        {
            var templateId = _configuration["MSG91:DefaultTemplateId"] ?? string.Empty;
            return await SendTemplateSmsAsync(phoneNumber, templateId, new Dictionary<string, string>
            {
                ["message"] = message
            });
        }

        public async Task<bool> SendTemplateSmsAsync(string phoneNumber, string templateId, Dictionary<string, string> variables)
        {
            var authKey = _configuration["MSG91:AuthKey"] ?? string.Empty;
            var senderId = _configuration["MSG91:SenderId"] ?? "RACETK";

            var formatted = FormatPhoneNumber(phoneNumber);
            var maskedPhone = MaskPhone(formatted);

            try
            {
                _httpClient.DefaultRequestHeaders.Remove("authkey");
                _httpClient.DefaultRequestHeaders.Add("authkey", authKey);

                var payload = new
                {
                    template_id = templateId,
                    sender = senderId,
                    short_url = "0",
                    mobiles = formatted,
                    VAR1 = variables.GetValueOrDefault("VAR1"),
                    VAR2 = variables.GetValueOrDefault("VAR2"),
                    VAR3 = variables.GetValueOrDefault("VAR3"),
                };

                var response = await _httpClient.PostAsJsonAsync(FlowApiUrl, payload);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SMS sent to {Phone} via template {TemplateId}", maskedPhone, templateId);
                    return true;
                }

                _logger.LogWarning("MSG91 returned {StatusCode} for {Phone}. Body: {Body}",
                    (int)response.StatusCode, maskedPhone, body);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SMS to {Phone} via template {TemplateId}", maskedPhone, templateId);
                return false;
            }
        }

        private static string FormatPhoneNumber(string phone)
        {
            phone = phone.Replace(" ", "").Replace("-", "").Replace("+", "");
            if (!phone.StartsWith("91") || phone.Length <= 10)
                phone = "91" + phone.TrimStart('0');
            return phone;
        }

        private static string MaskPhone(string phone)
        {
            if (phone.Length <= 4) return "****";
            return new string('*', phone.Length - 4) + phone[^4..];
        }
    }
}
