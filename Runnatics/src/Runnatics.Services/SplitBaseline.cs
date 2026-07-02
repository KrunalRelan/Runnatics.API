namespace Runnatics.Services
{
    /// <summary>
    /// Single source of truth for the NET split/cumulative baseline (client-confirmed rule).
    ///
    /// Stored SplitTimes.SplitTimeMs is GUN-based cumulative BY DESIGN — checkpoint ranks
    /// (CalculateSplitTimeRankingsAsync) and all legacy rows depend on that; do not change the
    /// storage semantics. Display/derived cumulative must instead be based on the RUNNER'S OWN
    /// VALID start crossing:
    ///
    ///   Start checkpoint row: Split = 00:00, Cumulative = 00:00. Always — a runner crossing the
    ///     mat 1:41 after the gun did NOT "take 1:41"; that's corral delay, not running time.
    ///   Cumulative at checkpoint N = crossing N − runner's own valid start crossing
    ///     (in stored terms: SplitTimeMs − baseline).
    ///   INVARIANT: cumulative at Finish == Results.NetTime (when NetTime is non-null).
    ///   No valid start (late-only placeholder, or no start row at all): baseline = the GUN
    ///     (offset 0) — consistent with the gun-clamped NetTime rule, so the invariant still
    ///     holds for a late-only finisher. The gun-to-start offset (Gun − Net) is a SEPARATE
    ///     value; it may be displayed, but never as a split/cumulative.
    ///
    /// Validity gate: stored split rows are never negative (both writers skip pre-gun-ms rows),
    /// so the ceiling is the only reachable edge — a start row is a valid start iff its gun
    /// offset ≤ LateStartCutOff (read through StartWindow's defaulting: null/0 → 1200s; a raw
    /// column read would make every start row "invalid" whenever the cutoff is unset).
    /// Max(0, ·) mirrors the Phase 2 gun clamp (BUG-27) for the pre-gun in-window case.
    /// </summary>
    public static class SplitBaseline
    {
        /// <summary>
        /// Gun-relative ms to subtract from stored gun-based cumulatives for this runner.
        /// <paramref name="startRowSplitTimeMs"/> = the START-gate split row's SplitTimeMs
        /// (null when the runner has no start row).
        /// </summary>
        public static long BaselineMs(long? startRowSplitTimeMs, int? lateStartCutOffSeconds)
        {
            if (!startRowSplitTimeMs.HasValue)
                return 0; // no start row → gun baseline (matches NetTime's fallback)

            var ceilingMs = StartWindow.LateSeconds(lateStartCutOffSeconds) * 1000L;
            return startRowSplitTimeMs.Value <= ceilingMs
                ? Math.Max(0, startRowSplitTimeMs.Value)   // valid start (clamped at the gun)
                : 0;                                        // late placeholder → gun baseline
        }

        /// <summary>Net cumulative for a row: stored gun-based ms minus the runner's baseline.</summary>
        public static long CumulativeMs(long? splitTimeMs, long baselineMs)
            => Math.Max(0, (splitTimeMs ?? 0) - baselineMs);
    }
}
