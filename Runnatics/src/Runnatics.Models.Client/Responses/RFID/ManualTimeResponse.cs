namespace Runnatics.Models.Client.Responses.RFID
{
    public class ManualTimeResponse
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string Bib { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int CheckpointId { get; set; }
        public string? CheckpointName { get; set; }
        public long ChipTimeMs { get; set; }
        public long CumulativeTimeMs { get; set; }
        public long SplitTimeMs { get; set; }
        public decimal? Pace { get; set; }
        public decimal? Speed { get; set; }
        public bool IsManual { get; set; } = true;

        // Populated only when the EDITED checkpoint is the race finish (the value just entered)
        public long? FinishTimeMs { get; set; }
        public string? FinishTime { get; set; }

        // #3 (2026-07-03): the COMPLETE updated result — populated on EVERY edit, reloaded AFTER
        // the recalc + re-rank, so the UI (chip-time header card + participants grid row) can
        // re-render from this payload without a second fetch. Ranks are null when the runner is
        // unranked (DNF/DNS/DSQ); times are the STORED post-recalc result times.
        public long? GunTimeMs { get; set; }
        public string? GunTime { get; set; }
        public long? NetTimeMs { get; set; }
        public string? NetTime { get; set; }
        public int? OverallRank { get; set; }
        public int? GenderRank { get; set; }
        public int? CategoryRank { get; set; }
        public int? TotalFinishers { get; set; }
        public string? Status { get; set; }

        /// <summary>Non-fatal note surfaced on a successful edit (e.g. an out-of-window start was discarded and the runner flagged DNS).</summary>
        public string? Warning { get; set; }
    }
}
