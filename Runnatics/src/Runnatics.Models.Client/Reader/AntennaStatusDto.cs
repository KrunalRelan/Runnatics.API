namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// DTO for antenna status
    /// </summary>
    public class AntennaStatusDto
    {
        public int Id { get; set; }
        public byte Port { get; set; }
        public string? Name { get; set; }
        public bool IsEnabled { get; set; }
        public int TxPowerCdBm { get; set; }
        public string? Position { get; set; }
    }
}
