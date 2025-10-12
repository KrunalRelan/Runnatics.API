using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class ForgotPasswordRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
