namespace Runnatics.Models.Client.Requests.RFID
{
    /// <summary>
    /// Body of POST api/RFID/live-readings — READINGS ONLY. The device identity travels
    /// as QUERY PARAMETERS (?deviceMac=…&amp;deviceName=…), and no event/race ids exist
    /// anywhere in the contract: the device resolves the event exactly like the offline
    /// import-auto upload (shared resolver), and races are resolved per read downstream.
    /// </summary>
    public class LiveReadingsRequest
    {
        public List<LiveReadingDto> Readings { get; set; } = [];
    }
}
