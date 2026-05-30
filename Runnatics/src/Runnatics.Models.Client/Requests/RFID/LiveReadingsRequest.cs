namespace Runnatics.Models.Client.Requests.RFID
{
    public class LiveReadingsRequest
    {
        /// <summary>Device MAC address — must match a registered Device record (e.g. "00162512dbb0").</summary>
        public string DeviceId { get; set; } = string.Empty;

        public List<LiveReadingDto> Readings { get; set; } = [];
    }
}
