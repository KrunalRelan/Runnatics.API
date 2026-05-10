namespace Runnatics.Models.Client.Public
{
    public class PublicResultFiltersDto
    {
        public List<int> Years { get; set; } = [];
        public List<PublicEventFilterItemDto> Events { get; set; } = [];
    }

    public class PublicEventFilterItemDto
    {
        public string EncryptedId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string EventDate { get; set; } = string.Empty;
        public int Year { get; set; }
    }
}
