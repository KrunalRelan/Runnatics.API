namespace Runnatics.Models.Client.Responses.Participants
{
    public class RfidRawReadingDto
    {
        public string Id { get; set; } = string.Empty;
        public string LocalTime { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string? Checkpoint { get; set; }
        /// <summary>
        /// Encrypted id of the checkpoint this read is assigned to (null if unassigned). Lets the
        /// "choose which read is the crossing" toggle target the override API for the read's OWN
        /// checkpoint (scoping is also enforced server-side).
        /// </summary>
        public string? CheckpointId { get; set; }
        /// <summary>
        /// True when this read's checkpoint has an active ManualTimeOverride for this participant
        /// (derived from the override row — the single source of truth). With IsNormalized this tells the
        /// UI whether the current crossing is an override (HasActiveOverride) or the dedup default
        /// (IsNormalized &amp;&amp; !HasActiveOverride) — so a "cycle back to auto" becomes a revert (DELETE),
        /// without the client predicting the dedup pick.
        /// </summary>
        public bool HasActiveOverride { get; set; }
        public decimal? CheckpointDistance { get; set; }
        public string Device { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string? GunTime { get; set; }
        public string? NetTime { get; set; }
        public string ChipId { get; set; } = string.Empty;
        public string ProcessResult { get; set; } = string.Empty;
        public bool IsManual { get; set; }
        public bool IsDuplicate { get; set; }
        public bool IsNormalized { get; set; }
        public bool IsMultipleEpc { get; set; }
    }
}
