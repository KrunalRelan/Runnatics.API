namespace Runnatics.Services
{
    /// <summary>
    /// THE stored derived-time math (Phase 2.45 reconciliation + unit pins).
    ///
    /// GunTime and NetTime on a ReadNormalized row are DERIVED values:
    ///   GunTime = crossing − gun
    ///   NetTime = crossing − the participant's NET BASELINE (their selected start crossing)
    /// Phase 2 computes them only for rows it CREATES and Phase 2.4 only for the overridden
    /// row itself — so a changed START crossing (toggle / typed edit / revert) left every
    /// OTHER row's NetTime frozen at the old baseline, and Phase 3 copies the finish row's
    /// NetTime into Results verbatim (bib 1002: two different starts → byte-identical net).
    /// Phase 2.45 (RFIDImportService.RecomputeDerivedTimesAsync) re-derives EVERY active row
    /// from the CURRENT crossings through these two functions — the same rules Phase 2 uses:
    ///   baseline = the IN-WINDOW selected start (StartWindow.SelectStartRead), gun-clamped
    ///              (BUG-27: a pre-gun start nets from the gun);
    ///   start rows present but none in-window → baseline = gun;
    ///   no start row at all → baseline = null → NetTime = null on non-start rows;
    ///   start-gate rows: NetTime = GunTime (the gun-to-mat offset);
    ///   negative net (crossing before baseline) → null, matching Phase 2's guard.
    /// </summary>
    public static class DerivedTimes
    {
        /// <summary>
        /// The participant's NET baseline from their CURRENT start-gate state.
        /// <paramref name="selectedInWindowStartChip"/> is the StartWindow.SelectStartRead
        /// winner over the start-gate rows (null = no in-window crossing).
        /// </summary>
        public static DateTime? NetBaseline(DateTime gun, bool hasStartRows, DateTime? selectedInWindowStartChip)
        {
            if (selectedInWindowStartChip.HasValue)
                return gun > selectedInWindowStartChip.Value ? gun : selectedInWindowStartChip.Value;
            return hasStartRows ? gun : (DateTime?)null;
        }

        /// <summary>Gun/Net for one stored crossing, from the current gun + baseline.</summary>
        public static (long GunMs, long? NetMs) ForRow(DateTime chip, DateTime gun, DateTime? baseline, bool isStartGateRow)
        {
            var gunMs = (long)(chip - gun).TotalMilliseconds;

            long? netMs;
            if (isStartGateRow)
            {
                netMs = gunMs;
            }
            else if (!baseline.HasValue)
            {
                netMs = null;
            }
            else
            {
                var n = (long)(chip - baseline.Value).TotalMilliseconds;
                netMs = n < 0 ? (long?)null : n;
            }

            return (gunMs, netMs);
        }
    }
}
