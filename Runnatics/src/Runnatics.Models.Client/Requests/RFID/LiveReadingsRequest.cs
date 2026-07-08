namespace Runnatics.Models.Client.Requests.RFID
{
    public class LiveReadingsRequest
    {
        /// <summary>
        /// Device MAC address (e.g. "00162512dbb0", separators tolerated). Resolves
        /// event → race → checkpoint exactly like the offline import-auto upload (shared
        /// resolver); no event/race ids are sent. Also accepts a registered device Name
        /// here for backward compatibility with older Pi payloads.
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Registered device Name (e.g. "Box 15") — the resolution FALLBACK when the MAC
        /// in <see cref="DeviceId"/> doesn't match a registered device. At least one of
        /// DeviceId / DeviceName must be provided; when both are sent, MAC wins.
        /// </summary>
        public string? DeviceName { get; set; }

        public List<LiveReadingDto> Readings { get; set; } = [];
    }
}
