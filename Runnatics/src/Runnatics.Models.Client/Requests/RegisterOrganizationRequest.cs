using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class RegisterOrganizationRequest
    {
        [Required]
        [StringLength(255)]
        public string OrganizationName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string AdminEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string AdminFirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string AdminLastName { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Phone]
        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? SubscriptionPlan { get; set; } = "Free";
    }
}