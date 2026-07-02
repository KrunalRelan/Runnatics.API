using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Guards the checkpoint→device configuration the RFID pipeline depends on.
    ///
    /// A malformed config must FAIL LOUDLY — reject the save / abort the reprocess with a
    /// message naming the offending rows — never be processed on a silent guess. Race 65
    /// (event 38) demonstrated why: duplicate primary Finish rows plus circular parent/child
    /// device references made device→checkpoint resolution order-dependent, so different
    /// reprocesses produced different wrong start times with no error anywhere.
    ///
    /// Checks (all over ACTIVE checkpoints of one race):
    ///   (a) duplicate PRIMARY checkpoints at the same DistanceFromStart — a second mat at
    ///       one location must be modeled as a CHILD (ParentDeviceId), not a second primary;
    ///   (b) circular parent/child device references (includes a device parented to itself);
    ///   (c) contradictory device roles — a device that is primary in one row and a child in
    ///       another, or a child of two different parents (IdentifySharedDevices assumes one
    ///       mapping and one parent per device; either contradiction makes resolution ambiguous);
    ///   (d) two checkpoint rows on the SAME device at the same DistanceFromStart — the
    ///       pass-ordinal position between them is undefined (LoopRaceCheckpointAssigner
    ///       hard-fails on this too).
    ///
    /// Callers: CheckpointService (reject at authoring time) and RFIDImportService
    /// (ProcessCompleteWorkflowAsync / AssignCheckpointsForLoopRaceAsync — abort the reprocess).
    /// </summary>
    public static class CheckpointConfigValidator
    {
        /// <summary>
        /// Validates one race's active checkpoint rows. Returns one human-readable message per
        /// violation (empty list = valid config).
        /// </summary>
        public static List<string> Validate(IReadOnlyCollection<Checkpoint> activeCheckpoints)
        {
            var violations = new List<string>();
            if (activeCheckpoints == null || activeCheckpoints.Count == 0)
                return violations;

            // ── (a) Duplicate primary checkpoints at the same distance ──
            var duplicatePrimaries = activeCheckpoints
                .Where(cp => !IsChild(cp))
                .GroupBy(cp => cp.DistanceFromStart)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicatePrimaries)
            {
                violations.Add(
                    $"Duplicate PRIMARY checkpoints at {group.Key} KM: {Describe(group)}. " +
                    "A second mat at the same location must be a CHILD row (set ParentDeviceId), not a second primary.");
            }

            // Role checks only apply to rows with a real device.
            var deviceRows = activeCheckpoints.Where(cp => cp.DeviceId > 0).ToList();

            // ── (b) Circular parent/child device references ──
            var parentEdges = deviceRows
                .Where(IsChild)
                .Select(cp => new { Child = cp.DeviceId, Parent = cp.ParentDeviceId!.Value })
                .Distinct()
                .ToList();

            foreach (var edge in parentEdges.Where(e => e.Child == e.Parent))
            {
                violations.Add($"Device {edge.Child} is configured as its own parent.");
            }

            var parentsByChild = parentEdges
                .Where(e => e.Child != e.Parent)
                .GroupBy(e => e.Child)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Parent).ToList());

            var reportedCycles = new HashSet<string>();
            foreach (var startDevice in parentsByChild.Keys)
            {
                var cycle = FindCycleFrom(startDevice, parentsByChild);
                if (cycle == null)
                    continue;

                // Canonical key so A→B→A and B→A→B report once.
                var cycleKey = string.Join(",", cycle.OrderBy(d => d));
                if (reportedCycles.Add(cycleKey))
                {
                    violations.Add(
                        $"Circular parent/child device references: {string.Join(" → ", cycle)} → {cycle[0]}. " +
                        "Exactly one device at a location is the primary; the other(s) point at it, never back.");
                }
            }

            // ── (c) Contradictory device roles ──
            var primaryDevices = deviceRows.Where(cp => !IsChild(cp)).Select(cp => cp.DeviceId).ToHashSet();
            var childDevices = deviceRows.Where(IsChild).Select(cp => cp.DeviceId).ToHashSet();

            foreach (var deviceId in primaryDevices.Intersect(childDevices).OrderBy(d => d))
            {
                var rows = deviceRows.Where(cp => cp.DeviceId == deviceId);
                violations.Add(
                    $"Device {deviceId} is PRIMARY in one checkpoint row and a CHILD in another: {Describe(rows)}. " +
                    "A device must be either the primary at its location(s) or a child of one — not both.");
            }

            foreach (var kvp in parentsByChild.Where(kvp => kvp.Value.Count > 1).OrderBy(kvp => kvp.Key))
            {
                violations.Add(
                    $"Device {kvp.Key} is a child of MULTIPLE parents ({string.Join(", ", kvp.Value.OrderBy(p => p))}). " +
                    "Checkpoint assignment supports exactly one parent per device.");
            }

            // ── (d) Same device, same distance, more than one row ──
            var sameDeviceSameDistance = deviceRows
                .GroupBy(cp => new { cp.DeviceId, cp.DistanceFromStart })
                .Where(g => g.Count() > 1);

            foreach (var group in sameDeviceSameDistance)
            {
                violations.Add(
                    $"Device {group.Key.DeviceId} has {group.Count()} checkpoint rows at the same distance " +
                    $"({group.Key.DistanceFromStart} KM): {Describe(group)}. " +
                    "Pass-ordinal assignment between equal distances is undefined — fix DistanceFromStart.");
            }

            return violations;
        }

        private static bool IsChild(Checkpoint cp) => cp.ParentDeviceId.HasValue && cp.ParentDeviceId.Value > 0;

        private static string Describe(IEnumerable<Checkpoint> checkpoints) =>
            string.Join("; ", checkpoints.Select(cp =>
                $"checkpoint {cp.Id} '{cp.Name}' ({cp.DistanceFromStart} KM, device {cp.DeviceId}" +
                (IsChild(cp) ? $", parent device {cp.ParentDeviceId})" : ")")));

        /// <summary>
        /// Walks child→parent edges from <paramref name="startDevice"/>; returns the device cycle
        /// (from the repeated device around to itself) when one is reachable, else null.
        /// Explores ALL parent edges so a cycle behind a multi-parent row is still detected.
        /// </summary>
        private static List<int>? FindCycleFrom(int startDevice, Dictionary<int, List<int>> parentsByChild)
        {
            var path = new List<int>();
            var onPath = new HashSet<int>();
            int repeatedDevice = 0;

            bool Dfs(int device)
            {
                if (!onPath.Add(device))
                {
                    repeatedDevice = device; // revisited a device on the current path → cycle
                    return true;
                }

                path.Add(device);

                if (parentsByChild.TryGetValue(device, out var parents))
                {
                    foreach (var parent in parents)
                    {
                        if (Dfs(parent))
                            return true;
                    }
                }

                onPath.Remove(device);
                path.RemoveAt(path.Count - 1);
                return false;
            }

            if (!Dfs(startDevice))
                return null;

            // The cycle proper starts at the repeated device (the walk may have a non-cyclic prefix,
            // e.g. 5 → 1 → 2 → 1 cycles as 1 → 2, not from 5).
            return path.Skip(path.IndexOf(repeatedDevice)).ToList();
        }
    }
}
