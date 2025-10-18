using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class RefreshTokenRequest
    {
        [Required(ErrorMessage = "Refresh token is required")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
