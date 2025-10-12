namespace Runnatics.Models.Client.Responses
{
    public class AuthenticationResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public UserResponse User { get; set; } = null!;
        public OrganizationResponse Organization { get; set; } = null!;
    }
}