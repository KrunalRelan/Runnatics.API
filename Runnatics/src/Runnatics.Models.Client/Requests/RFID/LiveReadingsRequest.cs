namespace Runnatics.Models.Client.Requests.RFID
{
    /// <summary>
    /// Body of POST api/RFID/live-readings. Device identity may arrive as QUERY
    /// PARAMETERS (?deviceMac=…&amp;deviceName=…) or in the BODY (<see cref="DeviceId"/> —
    /// the shape the Pi firmware actually sends — and/or <see cref="DeviceName"/>);
    /// every identifier goes through the ONE shared device→event resolver. No
    /// event/race ids exist anywhere in the contract: the device resolves the event
    /// exactly like the offline import-auto upload, and races are resolved per read
    /// downstream.
    /// </summary>
    public class LiveReadingsRequest
    {
        /// <summary>Device identifier sent in the body — MAC address or registered device name.</summary>
        public string? DeviceId { get; set; }

        /// <summary>Optional additional device-name identifier sent in the body.</summary>
        public string? DeviceName { get; set; }

        public List<LiveReadingDto> Readings { get; set; } = [];
    }
}
