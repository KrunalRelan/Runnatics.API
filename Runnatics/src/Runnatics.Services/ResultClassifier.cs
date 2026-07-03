using Runnatics.Models.Data.Entities;

namespace Runnatics.Services
{
    /// <summary>
    /// Final status of a participant after result calculation.
    /// </summary>
    public enum ParticipantOutcome
    {
        Finished,
        DNF,
        DNS
    }

    /// <summary>
    /// SINGLE SOURCE OF TRUTH for the Finished / DNF / DNS decision.
    ///
    /// STATUS DEFINITIONS (#7, client-confirmed 2026-07-03 — REWRITES the old truth table):
    ///   OK  (display label for stored "Finished") — the runner has VALID data at ALL mandatory
    ///        checkpoints.
    ///   DNF — ANY mandatory checkpoint's data is missing or invalid (but at least one mandatory
    ///        checkpoint HAS valid data).
    ///   DNS — NO valid data at ANY mandatory checkpoint.
    ///   Invalid reads (pre-floor, out-of-window, discarded by sequence/min-segment rules) do NOT
    ///   count as data — a runner with ONLY invalid reads is DNS.
    ///
    /// What this deliberately KILLED (previous truth-table rows, removed with client sign-off):
    ///   - FINISHER-SAFE / "Row-5 keep": a runner with no valid start but full finish data used to
    ///     be kept Finished (netting from the gun). Now: the start gate is mandatory and its data
    ///     is missing/invalid → DNF.
    ///   - LATE-ONLY-FINISHER keep: same fate — a start read past the ceiling is invalid → DNF.
    ///   - EARLY-TAINT DNS: a pre-floor start with finish data used to be DNS for the whole run.
    ///     Now the early read is simply "invalid data at the start gate" → DNF when other mandatory
    ///     gates have valid data; DNS only when the invalid read was their only data.
    ///   Expect reprocessed old events to flip some previously-Finished runners to DNF — that is
    ///   the new rule working, not a regression.
    ///
    /// Gate VALIDITY is the caller's job (this method only counts):
    ///   - START gate: valid iff the selected start crossing is inside
    ///     [gun − EarlyStartCutOff, gun + LateStartCutOff] (StartWindow.Contains; boundaries
    ///     inclusive; no-gun fallback = any read valid).
    ///   - Other mandatory gates: valid iff a normalized crossing exists at that DISTANCE
    ///     (any checkpoint at the distance — BUG-05/BUG-26), surviving sequence/min-segment rules.
    ///   - An impossible (negative) finish time = INVALID data at the finish gate.
    ///
    /// DSQ is NOT decided here — it is a manual override applied on top of the computed status.
    /// </summary>
    public static class ResultClassifier
    {
        /// <summary>
        /// The three-way rule: all mandatory gates valid → Finished; some → DNF; none → DNS.
        /// </summary>
        public static ParticipantOutcome Classify(int validMandatoryGates, int totalMandatoryGates)
        {
            if (totalMandatoryGates > 0 && validMandatoryGates >= totalMandatoryGates)
                return ParticipantOutcome.Finished;

            if (validMandatoryGates > 0)
                return ParticipantOutcome.DNF;

            return ParticipantOutcome.DNS;
        }

        /// <summary>
        /// The MANDATORY gate set (as DISTANCES — two devices at one distance are ONE logical gate):
        ///   { START gate } ∪ { IsMandatory-flagged distances } ∪ ({ finish } when none are flagged).
        ///
        /// The START gate (lowest DistanceFromStart) is IMPLICITLY mandatory always — the client
        /// rule "missing valid start = DNF" is meaningless otherwise, and most race configs do not
        /// flag it. Keyed on DISTANCE, never on device: on a shared start/finish mat the start gate
        /// is the distance-0 checkpoint, not whichever primary a device lookup lands on.
        /// </summary>
        public static List<decimal> MandatoryDistances(IReadOnlyCollection<Checkpoint> activeCheckpoints)
        {
            if (activeCheckpoints.Count == 0)
                return new List<decimal>();

            var distances = new SortedSet<decimal>
            {
                // Start gate — implicitly mandatory (decision confirmed 2026-07-03).
                activeCheckpoints.Min(cp => cp.DistanceFromStart)
            };

            var flagged = activeCheckpoints
                .Where(cp => cp.IsMandatory)
                .Select(cp => cp.DistanceFromStart)
                .ToList();

            if (flagged.Count > 0)
            {
                foreach (var d in flagged)
                    distances.Add(d);
            }
            else
            {
                // No flagged checkpoints → the finish (highest distance) is the fallback gate,
                // exactly as before — now alongside the implicit start.
                distances.Add(activeCheckpoints.Max(cp => cp.DistanceFromStart));
            }

            return distances.ToList();
        }
    }
}
