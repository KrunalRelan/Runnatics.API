using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Deterministic start/finish GATE selection over a race's active checkpoints — and the ONE
    /// LOGICAL GATE identity (race 66): a PRIMARY mat + its CHILD row(s) at the same distance are
    /// ONE gate, and everything that stores or reads a crossing must key it by the PRIMARY's id.
    ///
    /// Two rows can share the gate distance (a PRIMARY mat + its CHILD, e.g. race 65's 396 + 429
    /// at 0.0 KM). A bare OrderBy(DistanceFromStart).First() breaks that tie by DB return order —
    /// unstable, and normalization merges child rows INTO the primary, so the gate id must be the
    /// PRIMARY's. Tie-break here: distance → primary before child → lowest Id (fully stable).
    /// </summary>
    public static class CheckpointGates
    {
        public static Checkpoint? Start(IEnumerable<Checkpoint> activeCheckpoints) =>
            activeCheckpoints
                .OrderBy(cp => cp.DistanceFromStart)
                .ThenBy(ChildRank)
                .ThenBy(cp => cp.Id)
                .FirstOrDefault();

        public static Checkpoint? Finish(IEnumerable<Checkpoint> activeCheckpoints) =>
            activeCheckpoints
                .OrderByDescending(cp => cp.DistanceFromStart)
                .ThenBy(ChildRank)
                .ThenBy(cp => cp.Id)
                .FirstOrDefault();

        /// <summary>
        /// CHILD checkpoint id → its PRIMARY checkpoint id at the same gate: the parent row is the
        /// one whose DeviceId equals the child's ParentDeviceId at the same distance (0.001 KM
        /// tolerance). EXACTLY the Phase-2 merge rule and validator check (e) — one implementation
        /// so a consumer can never disagree with the merge. An unresolvable child (orphan — configs
        /// check (e) rejects) simply gets no entry: absent = already canonical.
        ///
        /// RACE-66 INVARIANT: one logical gate = primary + its children = ONE candidate set → ONE
        /// selection → ONE normalized row, ALWAYS under the PRIMARY checkpoint. A child checkpoint
        /// must never own a normalized row, a manual override, or a split. Every consumer that
        /// takes a checkpoint id from an assignment, an override, an existing normalized row or the
        /// UI must fold it through this map first.
        /// </summary>
        public static Dictionary<int, int> CanonicalGateMap(IReadOnlyCollection<Checkpoint> activeCheckpoints)
        {
            var map = new Dictionary<int, int>();
            foreach (var child in activeCheckpoints.Where(cp => ChildRank(cp) == 1))
            {
                var parent = activeCheckpoints.FirstOrDefault(cp =>
                    cp.DeviceId == child.ParentDeviceId!.Value &&
                    Math.Abs(cp.DistanceFromStart - child.DistanceFromStart) < 0.001m);

                if (parent != null)
                    map[child.Id] = parent.Id;
            }
            return map;
        }

        /// <summary>Folds a checkpoint id onto its primary gate id (identity for primaries and unmapped ids).</summary>
        public static int Canonical(IReadOnlyDictionary<int, int> canonicalGateMap, int checkpointId) =>
            canonicalGateMap.TryGetValue(checkpointId, out var primaryId) ? primaryId : checkpointId;

        private static int ChildRank(Checkpoint cp) =>
            cp.ParentDeviceId.HasValue && cp.ParentDeviceId.Value > 0 ? 1 : 0;
    }
}
