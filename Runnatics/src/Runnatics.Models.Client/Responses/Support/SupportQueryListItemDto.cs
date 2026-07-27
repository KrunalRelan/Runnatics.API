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

        /// <summary>Bind row badges to THIS, not to the label — labels are DB-defined.</summary>
        public int StatusId { get; set; }

        public string StatusName { get; set; } = string.Empty;
    }
}
