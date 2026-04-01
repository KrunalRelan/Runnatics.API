namespace Runnatics.Models.Client.Responses.Support
{
    public class SupportQueryCommentDto
    {
        public int Id { get; set; }
        public string CommentText { get; set; } = string.Empty;
        public int TicketStatusId { get; set; }
        public string TicketStatusName { get; set; } = string.Empty;
        public bool NotificationSent { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }
}
