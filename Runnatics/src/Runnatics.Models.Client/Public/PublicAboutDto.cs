namespace Runnatics.Models.Client.Public
{
    /// <summary>
    /// Everything the public About page needs in one call.
    /// Null text fields mean "not configured yet" — the client falls back to its
    /// built-in copy so the page is never empty.
    /// </summary>
    public class PublicAboutDto
    {
        public string? WhoWeAre { get; set; }

        public string? Mission { get; set; }

        public string? StoryImageBase64 { get; set; }

        public List<PublicFounderDto> Founders { get; set; } = [];
    }

    public class PublicFounderDto
    {
        public string Name { get; set; } = string.Empty;

        public string? Role { get; set; }

        public string? Bio { get; set; }

        public string? PhotoBase64 { get; set; }
    }
}
