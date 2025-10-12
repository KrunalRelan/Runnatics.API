using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class InviteUserRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^(Admin|Ops|Support|ReadOnly)$")]
        public string Role { get; set; } = "ReadOnly";

        public string? Message { get; set; }
    }
}