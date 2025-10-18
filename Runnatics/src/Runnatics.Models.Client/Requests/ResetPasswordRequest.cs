using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class ResetPasswordRequest
    {
        [Required]
        public string ResetToken { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8-100 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]+$",
            ErrorMessage = "New password must contain at least one uppercase letter, one lowercase letter, one number, and one special character (@$!%*?&).")]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare("NewPassword")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
