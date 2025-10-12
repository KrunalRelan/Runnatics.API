using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class ResetPasswordRequest
    {
        [Required]
        public string ResetToken { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare("NewPassword")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
