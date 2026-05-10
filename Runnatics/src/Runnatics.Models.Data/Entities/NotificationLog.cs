namespace Runnatics.Models.Data.Entities
{
    public class NotificationLog
    {
        public int Id { get; set; }
        public string Channel { get; set; } = string.Empty;    // SMS, Email
        public string EventType { get; set; } = string.Empty;  // RaceCompletion, CheckpointCrossing, SupportTicket
        public int? ParticipantId { get; set; }
        public int? RaceId { get; set; }
        public string Recipient { get; set; } = string.Empty;  // phone or email
        public bool Success { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime SentAt { get; set; }
    }
}
