namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Result of the ONE device→event resolution shared by the blind OFFLINE upload
    /// (import-auto) and the blind ONLINE live-readings ingest: a device identifier
    /// (MAC or registered name) resolves to the EVENT via the device's newest active
    /// checkpoint mapping. There is deliberately NO race here — the RACE is resolved
    /// per read, downstream (event-level batch → EPC → Participant → RaceId, then the
    /// device's serial → checkpoint within that race via Phase 1/1.5).
    /// </summary>
    public class DeviceEventResolution
    {
        public int DeviceDbId { get; set; }
        public string? DeviceMacAddress { get; set; }
        public string? DeviceName { get; set; }
        public int EventId { get; set; }
        public int CheckpointId { get; set; }

        /// <summary>Set = resolution failed; a client-safe message naming the device.</summary>
        public string? Error { get; set; }

        public bool Succeeded => Error == null;
    }
}
