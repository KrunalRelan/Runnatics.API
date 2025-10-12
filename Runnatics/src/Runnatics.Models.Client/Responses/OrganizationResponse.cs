namespace Runnatics.Models.Client.Responses
{
    public class OrganizationResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Website { get; set; }
        public string LogoUrl { get; set; }
        public string SubscriptionPlan { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }

        public int TotalUsers { get; set; }
        public int ActiveEvents { get; set; }
    }
}
