namespace Runnatics.Services.RFID
{
    /// <summary>An existing crossing at another gate (course position + time + display name).</summary>
    public sealed class CrossingNeighbor
    {
        public string Name { get; init; } = string.Empty;
        public decimal Distance { get; init; }
        public DateTime ChipTime { get; init; }
    }

    /// <summary>A sequence violation: the edited crossing conflicts with a neighboring gate's crossing.</summary>
    public sealed class SequenceViolation
    {
        public string ConflictName { get; init; } = string.Empty;
        public DateTime ConflictTime { get; init; }

        /// <summary>true → the edited time must be BEFORE the conflicting crossing (a higher gate);
        /// false → it must be AFTER it (a lower gate).</summary>
        public bool MustBeBefore { get; init; }
    }

    /// <summary>
    /// #2 SEQUENCE VALIDATION (client rule, 2026-07-03): a manually edited crossing at
    /// checkpoint N must be STRICTLY after every lower-distance crossing and STRICTLY before
    /// every higher-distance crossing of the same runner (equal timestamps violate — the order
    /// is strict). Violations are HARD 400s on the typed-manual-edit path, with a message
    /// naming the conflicting checkpoint and its time.
    ///
    /// Generalizes "after N−1, before N+1" to gap-tolerant course order: when the adjacent gate
    /// has no crossing, the nearest existing crossing on that side is the bound. The OFFLINE
    /// pipeline equivalent (discard the out-of-order reading, try the next candidate, DNF when
    /// none remains) lives in SequentialGateSelector.
    /// </summary>
    public static class CrossingSequence
    {
        /// <summary>The nearest violated neighbor, or null when the edited time fits the order.</summary>
        public static SequenceViolation? FindViolation(
            decimal editedDistance,
            DateTime editedCrossingUtc,
            IEnumerable<CrossingNeighbor> otherCrossings)
        {
            // A lower gate crossed AT/AFTER the edited time → edited must be AFTER it.
            var lowerConflict = otherCrossings
                .Where(c => c.Distance < editedDistance && c.ChipTime >= editedCrossingUtc)
                .OrderByDescending(c => c.ChipTime) // name the closest (latest) offender
                .FirstOrDefault();
            if (lowerConflict != null)
            {
                return new SequenceViolation
                {
                    ConflictName = lowerConflict.Name,
                    ConflictTime = lowerConflict.ChipTime,
                    MustBeBefore = false
                };
            }

            // A higher gate crossed AT/BEFORE the edited time → edited must be BEFORE it.
            var higherConflict = otherCrossings
                .Where(c => c.Distance > editedDistance && c.ChipTime <= editedCrossingUtc)
                .OrderBy(c => c.ChipTime) // name the closest (earliest) offender
                .FirstOrDefault();
            if (higherConflict != null)
            {
                return new SequenceViolation
                {
                    ConflictName = higherConflict.Name,
                    ConflictTime = higherConflict.ChipTime,
                    MustBeBefore = true
                };
            }

            return null;
        }
    }
}
