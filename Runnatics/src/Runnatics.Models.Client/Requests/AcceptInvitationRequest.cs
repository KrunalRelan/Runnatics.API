using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class AcceptInvitationRequest
    {
        [Required]
        public string InvitationToken { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
