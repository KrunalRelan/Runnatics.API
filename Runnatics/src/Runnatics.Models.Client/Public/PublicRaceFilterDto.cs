namespace Runnatics.Models.Client.Public
{
    public class PublicRaceFilterDto
    {
        public List<PublicRaceFilterItemDto> Races { get; set; } = [];
    }

    public class PublicRaceFilterItemDto
    {
        public string EncryptedRaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Distance { get; set; }
    }
}
