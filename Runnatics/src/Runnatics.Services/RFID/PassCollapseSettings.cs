namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Single source of truth for the pass-collapse settings defaults (mirrors StartWindow's
    /// pattern for the start-window cut-offs). Both values are stored in SECONDS on
    /// RaceSettings; the "&gt; 0" guard treats BOTH null AND 0 (and negatives) as "use default".
    ///
    ///   DedUpSeconds              — reads within this gap are the SAME physical crossing
    ///                               (one representative per pass); default 30s.
    ///   PassGapThresholdSeconds   — a gap larger than this separates two PASSES of the same
    ///                               shared mat (outbound → return); default 300s.
    /// </summary>
    public static class PassCollapseSettings
    {
        public const int DefaultDedupWindowSeconds = 30;
        public const int DefaultPassGapSeconds = 300;

        public static int DedupSeconds(int? dedUpSeconds) =>
            dedUpSeconds > 0 ? dedUpSeconds.Value : DefaultDedupWindowSeconds;

        public static int PassGapSeconds(int? passGapThresholdSeconds) =>
            passGapThresholdSeconds > 0 ? passGapThresholdSeconds.Value : DefaultPassGapSeconds;
    }
}
