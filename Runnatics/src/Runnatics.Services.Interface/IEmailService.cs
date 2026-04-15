namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Interface for email service operations
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Send password reset email with reset link
        /// </summary>
        Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string? resetUrl = null);

        /// <summary>
        /// Send user invitation email
        /// </summary>
        Task<bool> SendInvitationEmailAsync(string email, string invitationToken, string organizationName);

        /// <summary>
        /// Send welcome email to new users
        /// </summary>
        Task<bool> SendWelcomeEmailAsync(string email, string firstName, string organizationName);

        /// <summary>
        /// Send a generic email (plain text or HTML)
        /// </summary>
        Task<bool> SendAsync(string to, string subject, string body);

        /// <summary>
        /// Send an HTML email
        /// </summary>
        Task SendEmailAsync(string to, string subject, string htmlBody);

        /// <summary>
        /// Send an HTML email with optional CC and BCC
        /// </summary>
        Task SendEmailAsync(string to, string subject, string htmlBody, List<string>? cc = null, List<string>? bcc = null);
    }
}
