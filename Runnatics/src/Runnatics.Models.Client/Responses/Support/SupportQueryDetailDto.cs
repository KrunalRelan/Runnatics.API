namespace Runnatics.Models.Client.Responses.Support
{
    public class SupportQueryDetailDto
    {
        public int Id { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string SubmitterEmail { get; set; } = string.Empty;
        public int StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public int? AssignedToUserId { get; set; }
        public string? AssignedToName { get; set; }
        public int? QueryTypeId { get; set; }
        public string? QueryTypeName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<SupportQueryCommentDto> Comments { get; set; } = new();
    }
}
