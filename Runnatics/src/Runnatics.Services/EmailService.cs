using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
            var subject = "Reset your Runnatics password";
            var body = $"Click the link below to reset your password:\n\n{url}\n\nThis link expires in 1 hour.";
            return await SendAsync(email, subject, body);
        }

        public async Task<bool> SendInvitationEmailAsync(string email, string invitationToken, string organizationName)
        {
            var url = $"{_configuration["AppSettings:FrontendUrl"]}/accept-invitation?token={invitationToken}";
            var subject = $"You've been invited to join {organizationName} on Runnatics";
            var body = $"You have been invited to join {organizationName}.\n\nAccept your invitation:\n\n{url}";
            return await SendAsync(email, subject, body);
        }

        public async Task<bool> SendWelcomeEmailAsync(string email, string firstName, string organizationName)
        {
            var subject = $"Welcome to Runnatics, {firstName}!";
            var body = $"Hi {firstName},\n\nWelcome to {organizationName} on Runnatics. Your account is ready.";
            return await SendAsync(email, subject, body);
        }

        public Task<bool> SendAsync(string to, string subject, string body)
        {
            // TODO: replace with real SMTP / SendGrid / etc. implementation
            _logger.LogInformation("EMAIL to={To} subject={Subject}", to, subject);
            return Task.FromResult(true);
        }
    }
}
