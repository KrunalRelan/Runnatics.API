namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Pass-collapse / segment settings (mirrors StartWindow's pattern).
    ///
    /// ============================================================================
    /// #6 DedUpSeconds REDEFINITION (2026-07-03, client-confirmed — BREAKING)
    /// OLD RULE (removed): DedUpSeconds = "collapse rapid repeat reads at the SAME
    ///   checkpoint within DedUpSeconds (default 30s)" — consumed here via
    ///   DedupSeconds(int?) with a null/0 → 30s guard, feeding the within-pass
    ///   keep-LAST window (CollapseIntoPasses) and the legacy per-batch dedup.
    /// NEW RULE: DedUpSeconds = MINIMUM SEGMENT TIME between CONSECUTIVE
    ///   checkpoints (SequentialGateSelector): a crossing at checkpoint N+1 within
    ///   &lt; DedUpSeconds of checkpoint N's crossing is discarded; a later reading
    ///   ≥ DedUpSeconds is used; the gate is uninhabited (→ DNF) only if no valid
    ///   reading remains. null/0 = the feature is OFF entirely — the old null/0 →
    ///   30s default is REMOVED.
    /// Reason: client redefinition (rule-change pass #6). The internal
    ///   same-checkpoint collapse still exists but is FROZEN at the 30s constant
    ///   below (no longer settings-driven) — one-crossing-per-checkpoint is
    ///   guaranteed by pass-gap chaining, Step-5 dedup and Phase-2 selection, not
    ///   by this setting.
    /// ============================================================================
    ///
    ///   PassGapThresholdSeconds — a gap larger than this separates two PASSES of the same
    ///                             shared mat (outbound → return); default 300s ("&gt; 0" guard:
    ///                             null/0/negative → default).
    /// </summary>
    public static class PassCollapseSettings
    {
        /// <summary>
        /// The FROZEN internal same-checkpoint collapse window (within-pass representative
        /// rule in CollapseIntoPasses + legacy per-batch dedup). Intentionally a constant —
        /// RaceSettings.DedUpSeconds no longer feeds it (see the #6 redefinition above).
        /// </summary>
        public const int DefaultDedupWindowSeconds = 30;

        public const int DefaultPassGapSeconds = 300;

        public static int PassGapSeconds(int? passGapThresholdSeconds) =>
            passGapThresholdSeconds > 0 ? passGapThresholdSeconds.Value : DefaultPassGapSeconds;

        /// <summary>
        /// #6: RaceSettings.DedUpSeconds as the MINIMUM SEGMENT TIME between consecutive
        /// checkpoints. null/0/negative → null = feature OFF (NO default — deliberate).
        /// </summary>
        public static int? MinSegmentSeconds(int? dedUpSeconds) =>
            dedUpSeconds > 0 ? dedUpSeconds : null;
    }
}
