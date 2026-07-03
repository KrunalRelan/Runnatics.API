namespace Runnatics.Services.RFID
{
    /// <summary>One candidate crossing at a gate. Key = caller correlation id (e.g. RawReadId).</summary>
    public sealed class GateCandidate
    {
        public long Key { get; init; }
        public DateTime Time { get; init; }
    }

    /// <summary>A gate's candidate pool. Candidates MUST be in ascending time order.</summary>
    public sealed class GateInput
    {
        public int GateId { get; init; }
        public bool IsStartGate { get; init; }
        public IReadOnlyList<GateCandidate> Candidates { get; init; } = Array.Empty<GateCandidate>();
    }

    /// <summary>
    /// Per-participant SEQUENTIAL GATE SELECTION — the #6 minimum-segment rule and the #2
    /// offline sequence rule in one pass (gates in course/distance order).
    ///
    /// ============================================================================
    /// #6 DedUpSeconds REDEFINITION (2026-07-03, client-confirmed — BREAKING)
    /// OLD RULE (removed): "collapse rapid repeat reads at the SAME checkpoint within
    ///   DedUpSeconds (default 30s)" — the setting fed the within-pass keep-LAST window in
    ///   CollapseIntoPasses and the legacy per-batch dedup.
    /// NEW RULE: DedUpSeconds = MINIMUM SEGMENT TIME between CONSECUTIVE checkpoints. A
    ///   crossing at checkpoint N+1 within &lt; DedUpSeconds of checkpoint N's selected
    ///   crossing is DISCARDED; if a later reading ≥ DedUpSeconds exists it is used; the gate
    ///   is left uninhabited (no normalized row → #7 counts missing data → DNF) only when no
    ///   valid reading remains. null/0 = the feature is OFF entirely (the old null/0 → 30s
    ///   default is REMOVED).
    /// Reason: client redefinition (rule-change pass #6). The one-crossing-per-checkpoint
    /// guarantee does NOT depend on the old rule — it comes from pass-gap chaining, Step-5
    /// dedup and this selection (the internal same-checkpoint collapse is frozen at a 30s
    /// constant, no longer settings-driven).
    /// ============================================================================
    ///
    /// Selection semantics (deliberate, pinned by tests):
    ///   - START gate: the START SELECTION INVARIANT (StartWindow.SelectStartRead — LAST read
    ///     of the first in-window pass); no in-window read → the EARLIEST candidate is kept as
    ///     the INVALID placeholder (classification/display need to see it). The chain anchors
    ///     on the selected crossing either way — it is the runner's physical crossing.
    ///   - Every later gate: the EARLIEST candidate STRICTLY AFTER the previous selected
    ///     crossing (#2) and, when the minimum-segment rule is ON, at least minSegmentSeconds
    ///     after it (#6). Violating candidates are discarded.
    ///   - GREEDY, NO BACKTRACKING: picking the earliest valid candidate at gate N can never
    ///     hurt gate N+1 (a later choice only raises the bound), so greedy satisfies the
    ///     "any combination that satisfies the order" requirement; when a gate's only
    ///     candidates starve the NEXT gate, the next gate goes uninhabited → DNF — the client
    ///     rule accepts that, and the selector does not backtrack.
    ///   - An uninhabited gate does not break the chain: the next gate validates against the
    ///     LAST SELECTED crossing.
    /// </summary>
    public static class SequentialGateSelector
    {
        /// <summary>Returns GateId → chosen candidate Key. A gate absent from the result is uninhabited.</summary>
        public static Dictionary<int, long> SelectChain(
            IReadOnlyList<GateInput> gatesInCourseOrder,
            DateTime? validStartFloor,
            DateTime? validStartCeiling,
            int passGapSeconds,
            int? minSegmentSeconds)
        {
            var selected = new Dictionary<int, long>();
            DateTime? anchor = null;

            foreach (var gate in gatesInCourseOrder)
            {
                if (gate.Candidates.Count == 0)
                    continue;

                if (gate.IsStartGate)
                {
                    GateCandidate chosen;
                    if (validStartFloor.HasValue)
                    {
                        chosen = StartWindow.SelectStartRead(
                                     gate.Candidates,
                                     c => c.Time,
                                     validStartFloor.Value,
                                     validStartCeiling!.Value,
                                     passGapSeconds)
                                 ?? gate.Candidates[0]; // out-of-window → INVALID placeholder (earliest)
                    }
                    else
                    {
                        chosen = gate.Candidates[0]; // no window (no gun) → historical earliest
                    }

                    selected[gate.GateId] = chosen.Key;
                    anchor = chosen.Time;
                    continue;
                }

                var minTime = anchor;
                var candidate = gate.Candidates.FirstOrDefault(c =>
                    !minTime.HasValue ||
                    (c.Time > minTime.Value &&
                     (!(minSegmentSeconds > 0) ||
                      (c.Time - minTime.Value).TotalSeconds >= minSegmentSeconds!.Value)));

                if (candidate == null)
                    continue; // gate uninhabited — chain continues from the last selected crossing

                selected[gate.GateId] = candidate.Key;
                anchor = candidate.Time;
            }

            return selected;
        }
    }
}
