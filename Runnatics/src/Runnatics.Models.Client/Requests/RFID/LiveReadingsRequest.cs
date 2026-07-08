namespace Runnatics.Models.Client.Requests.RFID
{
    public class LiveReadingsRequest
    {
        /// <summary>
        /// Device identifier — the MAC address (e.g. "00162512dbb0", separators tolerated)
        /// or the registered device Name. Resolves event → race → checkpoint exactly like
        /// the offline import-auto upload (shared resolver); no event/race ids are sent.
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        public List<LiveReadingDto> Readings { get; set; } = [];
    }
}
