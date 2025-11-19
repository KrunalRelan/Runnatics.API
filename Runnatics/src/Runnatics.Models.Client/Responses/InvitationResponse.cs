namespace Runnatics.Models.Client.Responses
{
    public class InvitationResponse
    {
        public string InvitationId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string InvitationLink { get; set; } = string.Empty;
    }
}