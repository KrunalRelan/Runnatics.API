namespace Runnatics.Services
{
    /// <summary>
    /// Final status of a participant after result calculation (Phase 3).
    /// </summary>
    public enum ParticipantOutcome
    {
        Finished,
        DNF,
        DNS
    }

    /// <summary>
    /// SINGLE SOURCE OF TRUTH for the Finished / DNF / DNS decision (the start-window truth
    /// table). Extracted from RFIDImportService.CalculateRaceResultsAsync (Phase 3) so the
    /// table is unit-testable row by row; Phase 3 calls this per participant.
    ///
    /// Inputs (all derived by the caller from normalized data):
    ///   earliestStartRead   — the participant's EARLIEST normalized start-gate crossing
    ///                         (Phase 2 keeps the earliest VALID in-window read when one
    ///                         exists, else the earliest available read as an INVALID
    ///                         placeholder — so "early + in-window both present" resolves
    ///                         to the in-window read BEFORE this classifier runs);
    ///   validStartFloor / validStartCeiling — [gun − EarlyStartCutOff, gun + LateStartCutOff]
    ///                         via StartWindow (null when the race has no gun — fall back
    ///                         to "any read is a valid start");
    ///   allMandatoryCovered — detection at EVERY mandatory DISTANCE (any checkpoint at the
    ///                         distance satisfies the gate — BUG-05/BUG-26);
    ///   hasNegativeFinishTime — finish-gate GunTime &lt; 0 (impossible time, stray read).
    ///
    /// The table (order matters):
    ///   1. negative finish time                  → DNF   (never Finished, never 500s the race)
    ///   2. earliest start BEFORE floor           → DNS   (early taint — even with finish data)
    ///   3. earliest start in [floor, ceiling]    → Finished if all mandatory covered, else DNF
    ///   4. no valid start but all covered        → Finished (finisher-safe: late/missing start
    ///                                              read is not a "did not start"; nets from gun)
    ///   5. no valid start, not a finisher        → DNS
    /// </summary>
    public static class ResultClassifier
    {
        public static ParticipantOutcome Classify(
            DateTime? earliestStartRead,
            DateTime? validStartFloor,
            DateTime? validStartCeiling,
            bool allMandatoryCovered,
            bool hasNegativeFinishTime)
        {
            // Row 1: reached the finish gate with an impossible (negative) time → DNF.
            if (hasNegativeFinishTime)
                return ParticipantOutcome.DNF;

            // Row 2: start crossing BEFORE the valid-start floor (too early) → illegitimate
            // start → DNS for the WHOLE run, even if mandatory checkpoints / finish are present.
            if (validStartFloor.HasValue &&
                earliestStartRead.HasValue &&
                earliestStartRead.Value < validStartFloor.Value)
            {
                return ParticipantOutcome.DNS;
            }

            // Row 3: valid start = an in-window read (boundaries INCLUSIVE). When the window
            // can't be computed (no gun — shouldn't happen post-validation), any read counts.
            bool hasValidStart = earliestStartRead.HasValue &&
                (!validStartFloor.HasValue ||
                 (earliestStartRead.Value >= validStartFloor.Value &&
                  earliestStartRead.Value <= validStartCeiling!.Value));

            if (hasValidStart)
                return allMandatoryCovered ? ParticipantOutcome.Finished : ParticipantOutcome.DNF;

            // Row 4: no valid start (late-only, or no start read), but the runner covered every
            // mandatory distance → they demonstrably ran → keep Finished (finisher-safe).
            if (allMandatoryCovered)
                return ParticipantOutcome.Finished;

            // Row 5: no valid start AND not a finisher → Did Not Start.
            return ParticipantOutcome.DNS;
        }
    }
}
