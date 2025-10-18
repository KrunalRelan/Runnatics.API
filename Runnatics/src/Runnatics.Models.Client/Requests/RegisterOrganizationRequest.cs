using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests
{
    public class RegisterOrganizationRequest
    {
        [Required(ErrorMessage = "Organization name is required")]
        [StringLength(100, ErrorMessage = "Organization name cannot exceed 100 characters")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Domain is required")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Domain must be between 3-30 characters")]
        [RegularExpression("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", ErrorMessage = "Domain can only contain lowercase letters, numbers, and hyphens. Cannot start or end with hyphen.")]
        public string Domain { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required")]
        [Phone(ErrorMessage = "Invalid phone number format")]
        public string PhoneNumber { get; set; } = string.Empty;

        // [Url(ErrorMessage = "Invalid website URL format")]
        // public string Website { get; set; }

        // Admin User Details
        [Required(ErrorMessage = "Admin first name is required")]
        [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
        public string AdminFirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Admin last name is required")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
        public string AdminLastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Admin email is required")]
        [EmailAddress(ErrorMessage = "Invalid admin email format")]
        public string AdminEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8-100 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]+$",
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character")]
        public string AdminPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password confirmation is required")]
        [Compare("AdminPassword", ErrorMessage = "Password and confirmation do not match")]
        public string ConfirmPassword { get; set; } = string.Empty;

        // Optional: Subscription plan selection
        public string SubscriptionPlan { get; set; } = "starter";

        public Guid CreatedBy { get; set; }
    }
}