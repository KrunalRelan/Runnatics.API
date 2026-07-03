using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// THE device-serial → Device.Id resolution map (extracted verbatim from Phase 1.5's
    /// FIX #2/#9 lookup): full MAC, separator-stripped MAC, last-6 / last-4 suffix variants
    /// (TryAdd — the first, most-specific registration wins when suffixes collide), and
    /// friendly-name variants (with and without spaces). Case-insensitive.
    ///
    /// Consumers: Phase 1.5 (AssignCheckpointsForLoopRaceAsync) and ASSIGN-THEN-CHOOSE
    /// (RecordManualTimeAsync's unassigned chosen-read path) — one implementation, so serial
    /// resolution can never fork.
    /// </summary>
    public static class DeviceSerialResolver
    {
        public static Dictionary<string, int> BuildLookup(IEnumerable<Device> devices)
        {
            var deviceLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in devices)
            {
                if (!string.IsNullOrEmpty(device.DeviceMacAddress))
                {
                    var mac = device.DeviceMacAddress;
                    deviceLookup[mac] = device.Id;                          // full: "00162511ebf3"

                    // Strip colon/hyphen separators: "00:16:25:11:eb:f3" → "00162511ebf3"
                    var macStripped = mac.Replace(":", "").Replace("-", "");
                    if (macStripped.Length != mac.Length)
                        deviceLookup[macStripped] = device.Id;

                    // Last-6 suffix: "11ebf3" — TryAdd so a full-MAC match is never overwritten
                    if (macStripped.Length >= 6)
                        deviceLookup.TryAdd(macStripped[^6..], device.Id);

                    // Last-4 suffix: "ebf3" — common format stored in UploadBatches.DeviceId
                    if (macStripped.Length >= 4)
                        deviceLookup.TryAdd(macStripped[^4..], device.Id);
                }
                if (!string.IsNullOrEmpty(device.Name))
                {
                    deviceLookup[device.Name] = device.Id;                  // "Box 16" → 12
                    // Also register name without spaces: "Box16" → 12
                    var nameNoSpace = device.Name.Replace(" ", "");
                    if (nameNoSpace.Length != device.Name.Length)
                        deviceLookup.TryAdd(nameNoSpace, device.Id);
                }
            }
            return deviceLookup;
        }

        /// <summary>
        /// Resolve the FIRST matching serial (priority order — callers pass the batch serial
        /// before the read's own DeviceId) to a Device.Id. 0 = unresolved. Null/empty entries
        /// are skipped, so callers can pass navigation values without guarding.
        /// </summary>
        public static int ResolveDeviceId(Dictionary<string, int> lookup, params string?[] serialsInPriorityOrder)
        {
            foreach (var serial in serialsInPriorityOrder)
            {
                if (!string.IsNullOrEmpty(serial) && lookup.TryGetValue(serial, out var id))
                    return id;
            }
            return 0;
        }
    }
}
