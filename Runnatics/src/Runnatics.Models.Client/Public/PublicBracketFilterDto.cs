namespace Runnatics.Models.Client.Public
{
    public class PublicBracketFilterDto
    {
        public List<PublicBracketItemDto> Brackets { get; set; } = [];
    }

    public class PublicBracketItemDto
    {
        public string Name { get; set; } = string.Empty;   // e.g. "Male 18-29"
        public string Gender { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }
}
