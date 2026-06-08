namespace Runnatics.Models.Client.Responses.PiDevice
{
    public class PiEventDto
    {
        public string EncryptedId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<PiRaceDto> Races { get; set; } = [];
    }
}
