namespace Runnatics.Services
{
    /// <summary>
    /// Single source of truth for the VALID START WINDOW so status (RFIDImportService Phase 1.5/2/3)
    /// and display (ResultsService.LoadCheckpointTimesAsync) can never drift apart.
    ///
    ///   floor   = gun - EarlyStartCutOff   (default 300s)
    ///   ceiling = gun + LateStartCutOff    (default 1200s)
    ///
    /// Both cut-offs are stored in SECONDS. The "&gt; 0" guard treats BOTH null AND 0 as "use default".
    /// A start crossing is valid only when it falls within [floor, ceiling].
    /// </summary>
    public static class StartWindow
    {
        public const int DefaultEarlyCutOffSeconds = 300;
        public const int DefaultLateCutOffSeconds = 1200;

        public static int EarlySeconds(int? earlyCutOff) => earlyCutOff > 0 ? earlyCutOff.Value : DefaultEarlyCutOffSeconds;
        public static int LateSeconds(int? lateCutOff) => lateCutOff > 0 ? lateCutOff.Value : DefaultLateCutOffSeconds;

        /// <summary>[floor, ceiling] for a known gun time.</summary>
        public static (DateTime Floor, DateTime Ceiling) For(DateTime gun, int? earlyCutOff, int? lateCutOff)
            => (gun.AddSeconds(-EarlySeconds(earlyCutOff)), gun.AddSeconds(LateSeconds(lateCutOff)));

        /// <summary>[floor, ceiling] when the gun may be null (returns null,null if no gun).</summary>
        public static (DateTime? Floor, DateTime? Ceiling) For(DateTime? gun, int? earlyCutOff, int? lateCutOff)
        {
            if (!gun.HasValue) return (null, null);
            var (f, c) = For(gun.Value, earlyCutOff, lateCutOff);
            return (f, c);
        }
    }
}
