using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger) : IEmailService
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<EmailService> _logger = logger;

        public async Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string? resetUrl = null)
        {
            var url = resetUrl ?? $"{_configuration["AppSettings:FrontendUrl"]}/reset-password?token={resetToken}";
            var subject = "Reset your Racetik password";
            var htmlBody = $@"<p>Click the link below to reset your password:</p>
<p><a href=""{url}"">{url}</a></p>
<p>This link expires in 1 hour.</p>";
            return await SendAsync(email, subject, htmlBody);
        }

        public async Task<bool> SendInvitationEmailAsync(string email, string invitationToken, string organizationName)
        {
            var url = $"{_configuration["AppSettings:FrontendUrl"]}/accept-invitation?token={invitationToken}";
            var subject = $"You've been invited to join {organizationName} on Racetik";
            var htmlBody = $@"<p>You have been invited to join <strong>{organizationName}</strong>.</p>
<p><a href=""{url}"">Accept your invitation</a></p>";
            return await SendAsync(email, subject, htmlBody);
        }

        public async Task<bool> SendWelcomeEmailAsync(string email, string firstName, string organizationName)
        {
            var subject = $"Welcome to Racetik, {firstName}!";
            var htmlBody = $@"<p>Hi {firstName},</p>
<p>Welcome to <strong>{organizationName}</strong> on Racetik. Your account is ready.</p>";
            return await SendAsync(email, subject, htmlBody);
        }

        public async Task<bool> SendAsync(string to, string subject, string body)
        {
            try
            {
                await SendEmailAsync(to, subject, body);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To} with subject {Subject}", MaskEmail(to), subject);
                return false;
            }
        }

        public Task SendEmailAsync(string to, string subject, string htmlBody)
            => SendEmailAsync(to, subject, htmlBody, null, null);

        public async Task SendEmailAsync(string to, string subject, string htmlBody, List<string>? cc = null, List<string>? bcc = null)
        {
            var host = _configuration["Email:SmtpHost"] ?? "smtp.hostinger.com";
            var port = int.Parse(_configuration["Email:SmtpPort"] ?? "465");
            var username = _configuration["Email:SmtpUsername"] ?? string.Empty;
            var password = _configuration["Email:SmtpPassword"] ?? string.Empty;
            var fromAddress = _configuration["Email:FromAddress"] ?? username;
            var fromName = _configuration["Email:FromName"] ?? "Racetik";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(to));

            if (cc != null)
                foreach (var addr in cc)
                    message.Cc.Add(MailboxAddress.Parse(addr));

            if (bcc != null)
                foreach (var addr in bcc)
                    message.Bcc.Add(MailboxAddress.Parse(addr));

            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {To} with subject {Subject}", MaskEmail(to), subject);
        }

        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 1) return "***@***";
            return email[0] + new string('*', at - 1) + email[at..];
        }
    }
}
