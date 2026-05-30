namespace Runnatics.Models.Client.Requests.RFID
{
    public class LiveReadingDto
    {
        public string Epc { get; set; } = string.Empty;
        public long Time { get; set; }
        public int? Antenna { get; set; }
        public decimal? Rssi { get; set; }
        public int? Channel { get; set; }
    }
}
