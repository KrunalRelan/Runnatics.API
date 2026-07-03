namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// ASSIGN-THEN-CHOOSE (UI contract): a checkpoint an UNASSIGNED raw read may be chosen
    /// for — this race's active gates served by the read's device (resolved server-side via
    /// the ONE DeviceSerialResolver map, identical to RecordManualTimeAsync's validation).
    /// One candidate → the UI auto-targets it; several (shared start/finish mat) → inline
    /// gate picker; none → the toggle stays locked (device not mapped in this race).
    /// </summary>
    public class ChoosableCheckpointDto
    {
        /// <summary>Encrypted checkpoint id — what the UI passes as the manual-time target.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name, e.g. "Start (0 km)" — same form as the Checkpoint column.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
