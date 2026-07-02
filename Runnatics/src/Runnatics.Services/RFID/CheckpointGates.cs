using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Deterministic start/finish GATE selection over a race's active checkpoints.
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

        private static int ChildRank(Checkpoint cp) =>
            cp.ParentDeviceId.HasValue && cp.ParentDeviceId.Value > 0 ? 1 : 0;
    }
}
