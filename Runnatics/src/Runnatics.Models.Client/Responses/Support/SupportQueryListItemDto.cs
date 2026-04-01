namespace Runnatics.Models.Client.Responses.Support
{
    public class SupportQueryListItemDto
    {
        public int Id { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string SubmitterEmail { get; set; } = string.Empty;
        public int CommentCount { get; set; }
        public string LastUpdated { get; set; } = string.Empty;
        public string? AssignedToName { get; set; }
        public string StatusName { get; set; } = string.Empty;
    }
}
