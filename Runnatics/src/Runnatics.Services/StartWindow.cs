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

        /// <summary>
        /// START SELECTION INVARIANT (client-confirmed, HISTORICAL rule — changing it requires
        /// explicit client sign-off): among IN-WINDOW start reads, the start is the
        /// <b>LAST read of the FIRST in-window pass</b> — the runner LEAVING the mat, not the
        /// first detection while queued on it. The pass boundary is the pass-gap threshold
        /// (PassCollapseSettings.PassGapSeconds, default 300s): a later in-window blip past the
        /// gap is a DIFFERENT pass (e.g. the finish crossing on a shared mat), never the start.
        ///
        /// Rules preserved around this selection (this method decides only WHICH read wins):
        ///   - pre-floor reads are categorically different — they never anchor or extend the
        ///     first in-window pass, and their exclusion / DNS-taint handling is the caller's;
        ///   - a same-pass read PAST the ceiling extends the pass but is not eligible to win;
        ///   - validity ("≥1 in-window read") and the DNS truth table are unchanged.
        ///
        /// The ONLY selection implementation — Phase 1.5 (CollapseIntoPasses), Phase 2 (NetTime
        /// baseline + start-row normalization) all call this so the rule can never fork again.
        /// Returns null when the runner has no in-window read.
        /// </summary>
        public static T? SelectStartRead<T>(
            IEnumerable<T> readsInTimeOrder,
            Func<T, DateTime> readTime,
            DateTime floor,
            DateTime ceiling,
            int passGapSeconds) where T : class
        {
            T? lastInWindowOfFirstPass = null;
            DateTime passAnchor = default;

            foreach (var read in readsInTimeOrder)
            {
                var t = readTime(read);

                if (lastInWindowOfFirstPass == null)
                {
                    // Seeking the FIRST in-window read — it anchors the first in-window pass.
                    if (t < floor || t > ceiling)
                        continue;
                    lastInWindowOfFirstPass = read;
                    passAnchor = t;
                    continue;
                }

                if ((t - passAnchor).TotalSeconds > passGapSeconds)
                    break; // first in-window pass ended — later reads are separate passes

                passAnchor = t; // same pass — extend the chain
                if (t <= ceiling)
                    lastInWindowOfFirstPass = read; // only IN-WINDOW reads are eligible to win
            }

            return lastInWindowOfFirstPass;
        }
    }
}
