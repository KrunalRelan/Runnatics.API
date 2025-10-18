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
        /// <param name="email">User email address</param>
        /// <param name="resetToken">Password reset token</param>
        /// <param name="resetUrl">Optional custom reset URL</param>
        Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string? resetUrl = null);

        /// <summary>
        /// Send user invitation email
        /// </summary>
        /// <param name="email">User email address</param>
        /// <param name="invitationToken">Invitation token</param>
        /// <param name="organizationName">Organization name</param>
        Task<bool> SendInvitationEmailAsync(string email, string invitationToken, string organizationName);

        /// <summary>
        /// Send welcome email to new users
        /// </summary>
        /// <param name="email">User email address</param>
        /// <param name="firstName">User first name</param>
        /// <param name="organizationName">Organization name</param>
        Task<bool> SendWelcomeEmailAsync(string email, string firstName, string organizationName);
    }
}
