using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Loop race checkpoint assignment using turnaround-based algorithm.
    /// 
    /// 5-STEP ALGORITHM:
    /// ┌───────────────────────────────────────────────────────────────┐
    /// │ Step 1: Load Data (readings, checkpoints, device mappings)   │
    /// │ Step 2: Identify Turnaround (single-device checkpoint)       │
    /// │ Step 3: Calculate Turnaround Time per Participant            │
    /// │ Step 4: Assign Checkpoints (turnaround ref → chronological)  │
    /// │ Step 5: Deduplicate (Start=LAST, Others=EARLIEST)            │
    /// └───────────────────────────────────────────────────────────────┘
    /// 
    /// For loop races where a single device serves two checkpoints (e.g., Start + Finish),
    /// this uses the turnaround checkpoint as a reference point to determine outbound vs return.
    /// Devices in the same shared group (e.g., Device 11 & 12 both at Start/Finish)
    /// share a single chronological rank counter for fallback assignment.
    /// </summary>
    public class LoopRaceCheckpointAssigner
    {
        private readonly ILogger _logger;

        public LoopRaceCheckpointAssigner(ILogger logger)
        {
            _logger = logger;
        }

        #region Data Models

        /// <summary>
        /// Turnaround checkpoint config — the single device that maps to exactly one checkpoint.
        /// </summary>
        public class TurnaroundConfig
        {
            public int DeviceId { get; set; }
            public int CheckpointId { get; set; }
            public decimal DistanceFromStart { get; set; }
            public string? CheckpointName { get; set; }
        }

        /// <summary>
        /// Shared device mapping — a device that serves both an outbound and return checkpoint.
        /// </summary>
        public class SharedDeviceMapping
        {
            public int DeviceId { get; set; }
            public int OutboundCheckpointId { get; set; }
            public int ReturnCheckpointId { get; set; }
            public decimal OutboundDistance { get; set; }
            public decimal ReturnDistance { get; set; }
            /// <summary>
            /// Group key shared across parent + child devices at the same location.
            /// e.g., "StartFinish" for Device 11 (Box 15) and Device 12 (Box 16).
            /// Devices in the same group share a single chronological rank counter.
            /// </summary>
            public string SharedGroupKey { get; set; } = string.Empty;
        }

        /// <summary>
        /// Input reading with device info, ready for checkpoint assignment.
        /// </summary>
        public class ReadingInput
        {
            public long ReadingId { get; set; }
            public string Epc { get; set; } = string.Empty;
            public int DeviceId { get; set; }
            public string? DeviceSerial { get; set; }
            public DateTime ReadTimeUtc { get; set; }
        }

        /// <summary>
        /// Reading with checkpoint assignment result.
        /// </summary>
        public class AssignedReading
        {
            public long ReadingId { get; set; }
            public string Epc { get; set; } = string.Empty;
            public int DeviceId { get; set; }
            public DateTime ReadTimeUtc { get; set; }
            public int CheckpointId { get; set; }
            public string CheckpointName { get; set; } = string.Empty;
            public decimal DistanceFromStart { get; set; }
            /// <summary>
            /// TurnaroundReference | ChronologicalOrder | SingleDevice
            /// </summary>
            public string AssignmentMethod { get; set; } = string.Empty;
        }

        #endregion

        #region Step 2: Identify Turnaround Checkpoint

        /// <summary>
        /// STEP 2: Find the checkpoint whose device has a single checkpoint mapping (the turnaround).
        /// Only considers primary checkpoints (no child devices).
        /// </summary>
        public TurnaroundConfig? IdentifyTurnaroundCheckpoint(List<Checkpoint> checkpoints)
        {
            // Group primary checkpoints by DeviceId
            var singleDeviceGroup = checkpoints
                .Where(cp => cp.DeviceId > 0 && (!cp.ParentDeviceId.HasValue || cp.ParentDeviceId == 0))
                .GroupBy(cp => cp.DeviceId)
                .FirstOrDefault(g => g.Count() == 1);

            if (singleDeviceGroup == null)
            {
                _logger.LogWarning("No turnaround checkpoint found (no device with single checkpoint mapping)");
                return null;
            }

            var cp = singleDeviceGroup.First();
            var config = new TurnaroundConfig
            {
                DeviceId = cp.DeviceId,
                CheckpointId = cp.Id,
                DistanceFromStart = cp.DistanceFromStart,
                CheckpointName = cp.Name
            };

            _logger.LogInformation(
                "Step 2: Turnaround checkpoint identified: '{Name}' (ID:{Id}) at {Distance}km, Device {DeviceId}",
                cp.Name, cp.Id, cp.DistanceFromStart, cp.DeviceId);

            return config;
        }

        #endregion

        #region Step 2b: Identify Shared Devices

        /// <summary>
        /// STEP 2b: Build shared device mappings — devices mapped to 2 checkpoints (outbound + return).
        /// Assigns a SharedGroupKey so that parent + child devices at the same location share ranks.
        /// 
        /// Example grouping:
        ///   Device 11 (Box 15) → Start/Finish   → SharedGroupKey = "StartFinish"
        ///   Device 12 (Box 16) → Start/Finish   → SharedGroupKey = "StartFinish"  (child of 11)
        ///   Device 13 (Box 19) → 5KM/16.1KM     → SharedGroupKey = "5Km16Km"
        ///   Device 14 (Box 24) → 5KM/16.1KM     → SharedGroupKey = "5Km16Km"      (child of 13)
        /// </summary>
        public Dictionary<int, SharedDeviceMapping> IdentifySharedDevices(List<Checkpoint> checkpoints)
        {
            var result = new Dictionary<int, SharedDeviceMapping>();

            // ──────────────────────────────────────────────────────────────
            // 1. Build shared groups from PRIMARY checkpoints (DeviceId, no ParentDeviceId)
            // ──────────────────────────────────────────────────────────────
            var primarySharedGroups = checkpoints
                .Where(cp => cp.DeviceId > 0 && (!cp.ParentDeviceId.HasValue || cp.ParentDeviceId == 0))
                .GroupBy(cp => cp.DeviceId)
                .Where(g => g.Count() == 2)
                .ToList();

            // Map: DeviceId → SharedGroupKey
            var deviceToGroupKey = new Dictionary<int, string>();
            int groupIndex = 0;

            foreach (var group in primarySharedGroups)
            {
                var cps = group.OrderBy(cp => cp.DistanceFromStart).ToList();
                var (outbound, returnCp) = ResolveOutboundReturn(cps[0], cps[1], group.Key);

                // Generate group key from checkpoint names
                var groupKey = GenerateGroupKey(outbound, returnCp, groupIndex++);
                deviceToGroupKey[group.Key] = groupKey;

                result[group.Key] = new SharedDeviceMapping
                {
                    DeviceId = group.Key,
                    OutboundCheckpointId = outbound.Id,
                    ReturnCheckpointId = returnCp.Id,
                    OutboundDistance = outbound.DistanceFromStart,
                    ReturnDistance = returnCp.DistanceFromStart,
                    SharedGroupKey = groupKey
                };

                _logger.LogInformation(
                    "Shared device: Device {DeviceId} → Outbound '{OutName}' ({OutDist}km, ID:{OutId}) / Return '{RetName}' ({RetDist}km, ID:{RetId}), Group={Group}",
                    group.Key,
                    outbound.Name, outbound.DistanceFromStart, outbound.Id,
                    returnCp.Name, returnCp.DistanceFromStart, returnCp.Id,
                    groupKey);
            }

            // ──────────────────────────────────────────────────────────────
            // 2. Map CHILD checkpoints to the same shared group as their parent
            // ──────────────────────────────────────────────────────────────
            var childSharedGroups = checkpoints
                .Where(cp => cp.DeviceId > 0 && cp.ParentDeviceId.HasValue && cp.ParentDeviceId > 0)
                .GroupBy(cp => cp.DeviceId)
                .Where(g => g.Count() == 2)
                .ToList();

            foreach (var group in childSharedGroups)
            {
                var cps = group.OrderBy(cp => cp.DistanceFromStart).ToList();
                var (outbound, returnCp) = ResolveOutboundReturn(cps[0], cps[1], group.Key);

                // Find the parent device's group key
                var parentDeviceId = cps[0].ParentDeviceId!.Value;
                var groupKey = deviceToGroupKey.TryGetValue(parentDeviceId, out var parentKey)
                    ? parentKey
                    : GenerateGroupKey(outbound, returnCp, groupIndex++);

                deviceToGroupKey[group.Key] = groupKey;

                result[group.Key] = new SharedDeviceMapping
                {
                    DeviceId = group.Key,
                    OutboundCheckpointId = outbound.Id,
                    ReturnCheckpointId = returnCp.Id,
                    OutboundDistance = outbound.DistanceFromStart,
                    ReturnDistance = returnCp.DistanceFromStart,
                    SharedGroupKey = groupKey
                };

                _logger.LogInformation(
                    "Child shared device: Device {DeviceId} (parent:{ParentId}) → Group={Group}",
                    group.Key, parentDeviceId, groupKey);
            }

            return result;
        }

        /// <summary>
        /// Determines which checkpoint is outbound vs return by name first, then by distance.
        /// </summary>
        private (Checkpoint outbound, Checkpoint returnCp) ResolveOutboundReturn(
            Checkpoint cp1, Checkpoint cp2, int deviceId)
        {
            // Strategy 1: Match by name
            var startCp = new[] { cp1, cp2 }.FirstOrDefault(cp =>
                cp.Name?.Contains("Start", StringComparison.OrdinalIgnoreCase) == true ||
                cp.Name?.Contains("Begin", StringComparison.OrdinalIgnoreCase) == true);
            var finishCp = new[] { cp1, cp2 }.FirstOrDefault(cp =>
                cp.Name?.Contains("Finish", StringComparison.OrdinalIgnoreCase) == true ||
                cp.Name?.Contains("End", StringComparison.OrdinalIgnoreCase) == true);

            if (startCp != null && finishCp != null && startCp.Id != finishCp.Id)
                return (startCp, finishCp);

            // Strategy 2: Lower distance = outbound
            var ordered = new[] { cp1, cp2 }.OrderBy(cp => cp.DistanceFromStart).ToArray();

            if (ordered[0].DistanceFromStart == ordered[1].DistanceFromStart)
            {
                _logger.LogWarning(
                    "Device {DeviceId}: Both checkpoints have same distance ({Dist}km). " +
                    "Outbound/return assignment may be incorrect. Fix DistanceFromStart.",
                    deviceId, ordered[0].DistanceFromStart);
            }

            return (ordered[0], ordered[1]);
        }

        private static string GenerateGroupKey(Checkpoint outbound, Checkpoint returnCp, int fallbackIndex)
        {
            var outName = outbound.Name?.Replace(" ", "") ?? "Out";
            var retName = returnCp.Name?.Replace(" ", "") ?? "Ret";

            // e.g., "Start_Finish" or "5KM_16.1KM"
            if (outName.Length > 0 && retName.Length > 0)
                return $"{outName}_{retName}";

            return $"SharedGroup_{fallbackIndex}";
        }

        #endregion

        #region Step 3: Calculate Turnaround Times

        /// <summary>
        /// STEP 3: For each EPC, find the earliest reading on the turnaround device.
        /// </summary>
        public Dictionary<string, DateTime> CalculateTurnaroundTimesPerParticipant(
            List<ReadingInput> allReadings,
            int turnaroundDeviceId)
        {
            var result = allReadings
                .Where(r => r.DeviceId == turnaroundDeviceId)
                .GroupBy(r => r.Epc)
                .ToDictionary(
                    g => g.Key,
                    g => g.Min(r => r.ReadTimeUtc));

            _logger.LogInformation(
                "Step 3: Calculated turnaround times for {WithTurnaround}/{Total} unique EPCs",
                result.Count,
                allReadings.Select(r => r.Epc).Distinct().Count());

            return result;
        }

        /// <summary>
        /// STEP 3b: Median turnaround for participants without a turnaround reading.
        /// </summary>
        public DateTime? CalculateMedianTurnaround(
            Dictionary<string, DateTime> turnaroundTimes,
            DateTime raceStartTime)
        {
            if (!turnaroundTimes.Any())
                return null;

            var sorted = turnaroundTimes.Values.OrderBy(t => t).ToList();
            var median = sorted[sorted.Count / 2];

            _logger.LogInformation(
                "Step 3b: Median turnaround = {Time} ({Elapsed:F1} min from race start)",
                median.ToString("HH:mm:ss"), (median - raceStartTime).TotalMinutes);

            return median;
        }

        #endregion

        #region Step 4: Assign Checkpoints

        /// <summary>
        /// STEP 4: Assign checkpoints to ALL readings for ALL participants.
        /// 
        /// Priority 1: Use turnaround reference if participant has turnaround reading.
        ///              Reading BEFORE turnaround → Outbound checkpoint
        ///              Reading AFTER turnaround  → Return checkpoint
        /// 
        /// Priority 2: Chronological order within shared device GROUP.
        ///              1st reading across all devices in group → Outbound
        ///              2nd+ reading across all devices in group → Return
        /// 
        /// CRITICAL: Chronological ranking is per SHARED GROUP, not per device.
        /// Devices 11 and 12 both belong to "StartFinish" group and share a single rank counter.
        /// This matches the SQL: ROW_NUMBER() OVER (PARTITION BY Epc, SharedGroup ORDER BY ReadTimeUtc)
        /// </summary>
        public List<AssignedReading> AssignAllCheckpoints(
            Dictionary<string, List<ReadingInput>> readingsByEpc,
            TurnaroundConfig? turnaroundConfig,
            Dictionary<int, SharedDeviceMapping> sharedDevices,
            Dictionary<string, DateTime> turnaroundTimes,
            DateTime? medianTurnaround,
            Dictionary<int, List<Checkpoint>> singleDeviceCheckpoints)
        {
            var results = new List<AssignedReading>();
            int turnaroundAssignments = 0, chronologicalAssignments = 0, singleDeviceAssignments = 0;

            // Build reverse lookup: DeviceId → SharedGroupKey
            var deviceToGroup = new Dictionary<int, string>();
            foreach (var kvp in sharedDevices)
            {
                deviceToGroup[kvp.Key] = kvp.Value.SharedGroupKey;
            }

            foreach (var (epc, epcReadings) in readingsByEpc)
            {
                var sortedReadings = epcReadings.OrderBy(r => r.ReadTimeUtc).ToList();

                // Get this participant's turnaround time (own > median > null)
                DateTime? participantTurnaround = turnaroundTimes.TryGetValue(epc, out var tt)
                    ? tt
                    : medianTurnaround;

                bool hasTurnaround = participantTurnaround.HasValue;

                // ──────────────────────────────────────────────────────────
                // Pre-calculate chronological ranks within each SHARED GROUP
                // This matches the SQL:
                //   ROW_NUMBER() OVER (PARTITION BY Epc, SharedGroupKey ORDER BY ReadTimeUtc)
                // ──────────────────────────────────────────────────────────
                var groupRanks = CalculateSharedGroupRanks(sortedReadings, deviceToGroup);

                foreach (var reading in sortedReadings)
                {
                    // Case 1: Turnaround device → always single checkpoint
                    if (turnaroundConfig != null && reading.DeviceId == turnaroundConfig.DeviceId)
                    {
                        results.Add(new AssignedReading
                        {
                            ReadingId = reading.ReadingId,
                            Epc = epc,
                            DeviceId = reading.DeviceId,
                            ReadTimeUtc = reading.ReadTimeUtc,
                            CheckpointId = turnaroundConfig.CheckpointId,
                            CheckpointName = turnaroundConfig.CheckpointName ?? "Turnaround",
                            DistanceFromStart = turnaroundConfig.DistanceFromStart,
                            AssignmentMethod = "SingleDevice"
                        });
                        singleDeviceAssignments++;
                        continue;
                    }

                    // Case 2: Shared device → determine outbound vs return
                    if (sharedDevices.TryGetValue(reading.DeviceId, out var mapping))
                    {
                        bool isOutbound;
                        string method;

                        if (hasTurnaround)
                        {
                            // Priority 1: Turnaround reference
                            isOutbound = reading.ReadTimeUtc < participantTurnaround!.Value;
                            method = "TurnaroundReference";
                            turnaroundAssignments++;
                        }
                        else
                        {
                            // Priority 2: Chronological rank within shared group
                            var rank = groupRanks.TryGetValue(reading.ReadingId, out var r) ? r : 1;
                            isOutbound = rank == 1;
                            method = "ChronologicalOrder";
                            chronologicalAssignments++;
                        }

                        results.Add(new AssignedReading
                        {
                            ReadingId = reading.ReadingId,
                            Epc = epc,
                            DeviceId = reading.DeviceId,
                            ReadTimeUtc = reading.ReadTimeUtc,
                            CheckpointId = isOutbound ? mapping.OutboundCheckpointId : mapping.ReturnCheckpointId,
                            DistanceFromStart = isOutbound ? mapping.OutboundDistance : mapping.ReturnDistance,
                            CheckpointName = isOutbound ? "Outbound" : "Return",
                            AssignmentMethod = method
                        });
                        continue;
                    }

                    // Case 3: Non-shared, non-turnaround device (single checkpoint mapping)
                    if (singleDeviceCheckpoints.TryGetValue(reading.DeviceId, out var deviceCps) && deviceCps.Count == 1)
                    {
                        var cp = deviceCps[0];
                        results.Add(new AssignedReading
                        {
                            ReadingId = reading.ReadingId,
                            Epc = epc,
                            DeviceId = reading.DeviceId,
                            ReadTimeUtc = reading.ReadTimeUtc,
                            CheckpointId = cp.Id,
                            CheckpointName = cp.Name ?? "Unknown",
                            DistanceFromStart = cp.DistanceFromStart,
                            AssignmentMethod = "SingleDeviceMapping"
                        });
                        singleDeviceAssignments++;
                        continue;
                    }

                    // Case 4: Unknown device
                    _logger.LogWarning(
                        "EPC {Epc}: Reading {ReadingId} at {Time} from device {DeviceId} has no checkpoint mapping",
                        epc, reading.ReadingId, reading.ReadTimeUtc.ToString("HH:mm:ss"), reading.DeviceId);
                }
            }

            _logger.LogInformation(
                "Step 4: Assigned {Total} readings — TurnaroundRef={Turnaround}, Chronological={Chrono}, SingleDevice={Single}",
                results.Count, turnaroundAssignments, chronologicalAssignments, singleDeviceAssignments);

            return results;
        }

        /// <summary>
        /// Calculate chronological rank within each SHARED GROUP for a participant.
        /// 
        /// Devices in the same group share a single rank counter:
        ///   Device 11 reading at 06:01 → StartFinish rank 1 → Start
        ///   Device 12 reading at 06:02 → StartFinish rank 2 → Finish (if no turnaround ref)
        ///   Device 13 reading at 06:28 → 5Km16Km rank 1    → 5KM
        ///   Device 14 reading at 07:30 → 5Km16Km rank 2    → 16.1KM
        /// 
        /// Returns: Dictionary of ReadingId → rank within its shared group
        /// </summary>
        private Dictionary<long, int> CalculateSharedGroupRanks(
            List<ReadingInput> sortedReadings,
            Dictionary<int, string> deviceToGroup)
        {
            var ranks = new Dictionary<long, int>();

            // Group readings by their shared group key, then rank chronologically
            var readingsByGroup = sortedReadings
                .Where(r => deviceToGroup.ContainsKey(r.DeviceId))
                .GroupBy(r => deviceToGroup[r.DeviceId]);

            foreach (var group in readingsByGroup)
            {
                int rank = 1;
                foreach (var reading in group.OrderBy(r => r.ReadTimeUtc))
                {
                    ranks[reading.ReadingId] = rank++;
                }
            }

            return ranks;
        }

        #endregion

        #region Step 5: Deduplicate
        // ╔══════════════════════════════════════════════════════════════════════════╗
        // ║ BUG 1 — LoopRaceCheckpointAssigner.cs                                  ║
        // ║ Step 5: DeduplicateAssignedReadings                                     ║
        // ║                                                                          ║
        // ║ PROBLEM: Groups by (Epc, CheckpointId). Start (CP 4314, Dev 11) and     ║
        // ║ Start-child (CP 4315, Dev 12) survive as separate groups. The earlier    ║
        // ║ weak read from Dev 12 becomes the displayed Start time.                  ║
        // ║                                                                          ║
        // ║ FIX: Group by (Epc, LogicalGroup) where LogicalGroup merges checkpoints  ║
        // ║ at the same DistanceFromStart. LAST is picked across BOTH devices.       ║
        // ║                                                                          ║
        // ║ IMPACT: Fixes 9 Start discrepancies (1-40s) in 21KM                     ║
        // ╚══════════════════════════════════════════════════════════════════════════╝

        // ── REPLACE the entire DeduplicateAssignedReadings method with: ──

        public List<AssignedReading> DeduplicateAssignedReadings(
            List<AssignedReading> readings,
            List<Checkpoint> checkpoints)
        {
            // Identify start checkpoint IDs (distance = 0 or name contains "Start")
            var startCheckpointIds = checkpoints
                .Where(cp =>
                    cp.DistanceFromStart == 0 ||
                    (cp.Name?.Contains("Start", StringComparison.OrdinalIgnoreCase) == true &&
                     cp.Name?.Contains("Finish", StringComparison.OrdinalIgnoreCase) != true))
                .Select(cp => cp.Id)
                .ToHashSet();

            // Remove any finish checkpoints that got included
            var finishIds = checkpoints
                .Where(cp => cp.Name?.Contains("Finish", StringComparison.OrdinalIgnoreCase) == true)
                .Select(cp => cp.Id)
                .ToHashSet();
            startCheckpointIds.ExceptWith(finishIds);

            // ── Build logical checkpoint groups ──
            // Checkpoints at the same DistanceFromStart are the same physical location
            // and should be treated as ONE logical checkpoint for dedup purposes.
            // e.g., Start (CP 4314, Dev 11) and Start-child (CP 4315, Dev 12)
            //        both at distance=0 → same logical group
            var cpToLogicalGroup = BuildLogicalCheckpointGroups(checkpoints);

            _logger.LogInformation(
                "Step 5: Dedup config — Start CPs (keep LAST): [{StartIds}], " +
                "Logical groups: {GroupCount}, All others: keep EARLIEST",
                string.Join(", ", startCheckpointIds),
                cpToLogicalGroup.Values.Distinct().Count());

            var result = readings
                .GroupBy(r => new
                {
                    r.Epc,
                    LogicalGroup = cpToLogicalGroup.GetValueOrDefault(r.CheckpointId, r.CheckpointId)
                })
                .Select(g =>
                {
                    // Check if ANY checkpoint in this group is a Start checkpoint
                    var isStart = g.Any(r => startCheckpointIds.Contains(r.CheckpointId));

                    var selected = isStart
                        ? g.OrderByDescending(r => r.ReadTimeUtc).First()  // Start → LAST
                        : g.OrderBy(r => r.ReadTimeUtc).First();            // Others → EARLIEST

                    if (g.Count() > 1)
                    {
                        _logger.LogDebug(
                            "Dedup: EPC {Epc} logical group {Group} — {Count} readings from CPs [{CpIds}], " +
                            "kept {Rule} at {Time} (CP {WinnerCp})",
                            g.Key.Epc, g.Key.LogicalGroup, g.Count(),
                            string.Join(",", g.Select(r => r.CheckpointId).Distinct()),
                            isStart ? "LAST" : "EARLIEST",
                            selected.ReadTimeUtc.ToString("HH:mm:ss"),
                            selected.CheckpointId);
                    }

                    return selected;
                })
                .ToList();

            var removed = readings.Count - result.Count;
            if (removed > 0)
            {
                _logger.LogInformation(
                    "Step 5: Deduplication {Original} → {Deduped} (removed {Removed} duplicates)",
                    readings.Count, result.Count, removed);
            }

            return result;
        }

        /// <summary>
        /// Build a mapping from CheckpointId → LogicalGroupId.
        /// Checkpoints at the same DistanceFromStart are grouped together.
        /// The lowest CheckpointId in each group becomes the representative.
        /// </summary>
        private Dictionary<int, int> BuildLogicalCheckpointGroups(List<Checkpoint> checkpoints)
        {
            var cpToGroup = new Dictionary<int, int>();

            var byDistance = checkpoints
                .GroupBy(cp => cp.DistanceFromStart)
                .ToList();

            foreach (var distGroup in byDistance)
            {
                var groupId = distGroup.Min(cp => cp.Id);
                foreach (var cp in distGroup)
                {
                    cpToGroup[cp.Id] = groupId;
                }
            }

            return cpToGroup;
        }
        #endregion
    }
}