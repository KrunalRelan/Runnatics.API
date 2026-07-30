namespace Runnatics.Models.Client.Responses.About
{
    /// <summary>Admin editor view of the About page content.</summary>
    public class AboutContentDto
    {
        public string? WhoWeAre { get; set; }

        public string? Mission { get; set; }

        public string? StoryImageBase64 { get; set; }

        public List<FounderDto> Founders { get; set; } = [];
    }

    public class FounderDto
    {
        /// <summary>Encrypted id.</summary>
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Role { get; set; }

        public string? Bio { get; set; }

        public string? PhotoBase64 { get; set; }

        public int DisplayOrder { get; set; }
    }
}
