using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class LogoutRequest
    {
        [Required(ErrorMessage = "Refresh token is required")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
