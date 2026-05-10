namespace Runnatics.Models.Client.Notifications
{
    public class NotificationResult
    {
        public bool Success { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }

        public static NotificationResult Ok(string? providerId = null) =>
            new() { Success = true, ProviderMessageId = providerId };

        public static NotificationResult Fail(string error) =>
            new() { Success = false, ErrorMessage = error };
    }
}
