using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Shared-device checkpoint assignment (N-checkpoint, pass-ordinal based).
    /// 
    /// 5-STEP ALGORITHM:
    /// ┌───────────────────────────────────────────────────────────────┐
    /// │ Step 1: Load Data (readings, checkpoints, device mappings)   │
    /// │ Step 2: Identify Turnaround (single-device checkpoint)       │
    /// │ Step 3: Calculate Turnaround Time per Participant            │
    /// │ Step 4: Assign Checkpoints (pass ordinal → ordered list)     │
    /// │ Step 5: Deduplicate (Start=LAST, Others=EARLIEST)            │
    /// └───────────────────────────────────────────────────────────────┘
    ///
    /// A device that serves N checkpoints (N >= 2) holds them ordered by DistanceFromStart;
    /// each pass-gap-separated pass maps by ordinal into that list — Sequential (point-to-point,
    /// extra passes clamp to last) or Cyclic (loops, modulo), driven by RaceSettings.HasLoops.
    /// N=2 out-and-back is the special case identical to the legacy outbound/return behavior;
    /// turnaround reference and chronological group rank remain as fallbacks when no pass
    /// ordinal was precomputed. Devices in the same shared group (parent + child at the same
    /// location) share a single chronological rank counter and pass counter.
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
        /// How a pass ordinal maps to a checkpoint index for a shared (multi-checkpoint) device.
        /// Sequential: point-to-point — pass N → checkpoint[min(N, count-1)] (extra passes clamp to last).
        /// Cyclic: loop with reused checkpoint rows — pass N → checkpoint[N % count] (wraps).
        /// Driven by RaceSettings.HasLoops (true → Cyclic, false/null → Sequential).
        /// NOTE: true cyclic persistence is limited downstream — Step 5 dedup and Phase 2
        /// normalization keep ONE reading per (participant, checkpoint). Loop races should
        /// model laps as DISTINCT checkpoint rows (GenerateLoopCheckpoints), which Sequential
        /// handles end-to-end. See .claude/design/ISSUE-1-checkpoint-assignment-redesign.md.
        /// </summary>
        public enum AssignmentMode
        {
            Sequential,
            Cyclic
        }

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
        /// One checkpoint position in a shared device's ordered checkpoint list.
        /// </summary>
        public class CheckpointSlot
        {
            public int CheckpointId { get; set; }
            public decimal Distance { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        /// <summary>
        /// Shared device mapping — a device that serves N checkpoints (N >= 2) along the course.
        /// Checkpoints are ordered by DistanceFromStart; the pass ordinal indexes into the list
        /// (clamped for Sequential, modulo for Cyclic).
        /// e.g., 7th GGHM 21KM: Box-1 → [Start 0km, 10.5km, Finish 21.1km] (N=3, Sequential).
        /// </summary>
        public class SharedDeviceMapping
        {
            public int DeviceId { get; set; }

            /// <summary>Ordered by DistanceFromStart asc (tiebreak Name, then Id). Index = pass ordinal target.</summary>
            public List<CheckpointSlot> Checkpoints { get; set; } = new();

            public AssignmentMode Mode { get; set; } = AssignmentMode.Sequential;

            /// <summary>
            /// Group key shared across parent + child devices at the same location(s).
            /// Devices in the same group share a single chronological rank counter
            /// and a single pass counter.
            /// </summary>
            public string SharedGroupKey { get; set; } = string.Empty;

            public int Count => Checkpoints.Count;

            public bool StartsAtZero => Checkpoints.Count > 0 && Checkpoints[0].Distance == 0;

            /// <summary>
            /// Maps a 0-based pass ordinal to a checkpoint index.
            /// Sequential clamps extra passes to the last checkpoint; Cyclic wraps.
            /// </summary>
            public int IndexForPass(int pass)
            {
                if (Checkpoints.Count == 0)
                    return 0;
                return Mode == AssignmentMode.Cyclic
                    ? ((pass % Checkpoints.Count) + Checkpoints.Count) % Checkpoints.Count
                    : Math.Min(Math.Max(pass, 0), Checkpoints.Count - 1);
            }
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
            /// <summary>
            /// Pre-computed 0-based pass ordinal within the reading's shared group
            /// (pass-gap separated). null = no override (use turnaround/chronological fallback).
            /// </summary>
            public int? PassIndexOverride { get; set; }
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
        /// STEP 2b: Build shared device mappings — devices mapped to N checkpoints (N >= 2),
        /// ordered by DistanceFromStart. Assigns a SharedGroupKey so that parent + child
        /// devices at the same location(s) share ranks and pass counters.
        ///
        /// Example grouping (out-and-back, N=2):
        ///   Device 11 (Box 15) → Start/Finish   → SharedGroupKey = "Start_Finish"
        ///   Device 12 (Box 16) → Start/Finish   → SharedGroupKey = "Start_Finish"  (child of 11)
        /// Example (point-to-point reuse, N=3 — 7th GGHM):
        ///   Device 1 (Box 01)  → Start/10.5KM/Finish → SharedGroupKey = "Start_10.5KM_Finish"
        /// </summary>
        public Dictionary<int, SharedDeviceMapping> IdentifySharedDevices(
            List<Checkpoint> checkpoints,
            AssignmentMode mode = AssignmentMode.Sequential)
        {
            var result = new Dictionary<int, SharedDeviceMapping>();

            // ──────────────────────────────────────────────────────────────
            // 1. Build shared groups from PRIMARY checkpoints (DeviceId, no ParentDeviceId)
            // ──────────────────────────────────────────────────────────────
            var primarySharedGroups = checkpoints
                .Where(cp => cp.DeviceId > 0 && (!cp.ParentDeviceId.HasValue || cp.ParentDeviceId == 0))
                .GroupBy(cp => cp.DeviceId)
                .Where(g => g.Count() >= 2)
                .ToList();

            // Map: DeviceId → SharedGroupKey
            var deviceToGroupKey = new Dictionary<int, string>();
            int groupIndex = 0;

            foreach (var group in primarySharedGroups)
            {
                var slots = OrderCheckpointsByDistance(group, group.Key);

                // Generate group key from checkpoint names
                var groupKey = GenerateGroupKey(slots, groupIndex++);
                deviceToGroupKey[group.Key] = groupKey;

                result[group.Key] = new SharedDeviceMapping
                {
                    DeviceId = group.Key,
                    Checkpoints = slots,
                    Mode = mode,
                    SharedGroupKey = groupKey
                };

                _logger.LogInformation(
                    "Shared device: Device {DeviceId} → {Count} checkpoints [{Slots}], Mode={Mode}, Group={Group}",
                    group.Key,
                    slots.Count,
                    string.Join(" → ", slots.Select(s => $"'{s.Name}' ({s.Distance}km, ID:{s.CheckpointId})")),
                    mode,
                    groupKey);
            }

            // ──────────────────────────────────────────────────────────────
            // 2. Map CHILD checkpoints to the same shared group as their parent
            // ──────────────────────────────────────────────────────────────
            var childSharedGroups = checkpoints
                .Where(cp => cp.DeviceId > 0 && cp.ParentDeviceId.HasValue && cp.ParentDeviceId > 0)
                .GroupBy(cp => cp.DeviceId)
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var group in childSharedGroups)
            {
                var slots = OrderCheckpointsByDistance(group, group.Key);

                // Find the parent device's group key
                var parentDeviceId = group.First().ParentDeviceId!.Value;
                var groupKey = deviceToGroupKey.TryGetValue(parentDeviceId, out var parentKey)
                    ? parentKey
                    : GenerateGroupKey(slots, groupIndex++);

                deviceToGroupKey[group.Key] = groupKey;

                result[group.Key] = new SharedDeviceMapping
                {
                    DeviceId = group.Key,
                    Checkpoints = slots,
                    Mode = mode,
                    SharedGroupKey = groupKey
                };

                _logger.LogInformation(
                    "Child shared device: Device {DeviceId} (parent:{ParentId}) → {Count} checkpoints, Group={Group}",
                    group.Key, parentDeviceId, slots.Count, groupKey);
            }

            return result;
        }

        /// <summary>
        /// Orders a device's checkpoints by DistanceFromStart (tiebreak: Name, then Id) and
        /// projects them to CheckpointSlots. Distance is authoritative for ordinal position;
        /// equal distances are warned (ordinal order between them is ambiguous).
        /// </summary>
        private List<CheckpointSlot> OrderCheckpointsByDistance(IEnumerable<Checkpoint> cps, int deviceId)
        {
            var ordered = cps
                .OrderBy(cp => cp.DistanceFromStart)
                .ThenBy(cp => cp.Name)
                .ThenBy(cp => cp.Id)
                .ToList();

            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].DistanceFromStart == ordered[i - 1].DistanceFromStart)
                {
                    _logger.LogWarning(
                        "Device {DeviceId}: Checkpoints '{Cp1}' and '{Cp2}' have the same distance ({Dist}km). " +
                        "Pass-ordinal assignment between them may be incorrect. Fix DistanceFromStart.",
                        deviceId, ordered[i - 1].Name, ordered[i].Name, ordered[i].DistanceFromStart);
                }
            }

            return ordered
                .Select(cp => new CheckpointSlot
                {
                    CheckpointId = cp.Id,
                    Distance = cp.DistanceFromStart,
                    Name = cp.Name ?? string.Empty
                })
                .ToList();
        }

        private static string GenerateGroupKey(List<CheckpointSlot> slots, int fallbackIndex)
        {
            var names = slots
                .Select(s => s.Name.Replace(" ", ""))
                .Where(n => n.Length > 0)
                .ToList();

            // e.g., "Start_Finish" or "Start_10.5KM_Finish"
            if (names.Count == slots.Count && names.Count > 0)
                return string.Join("_", names);

            return $"SharedGroup_{fallbackIndex}";
        }

        #endregion

        #region Step 3: Calculate Turnaround Times

        /// <summary>
        /// STEP 3: For each EPC, find the earliest reading on the turnaround device.
        /// </summary>
        public Dictionary<string, DateTime> CalculateTurnaroundTimesPerParticipant(List<ReadingInput> allReadings, HashSet<int> turnaroundDeviceIds)
        {
            var result = allReadings
                .Where(r => turnaroundDeviceIds.Contains(r.DeviceId))
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
        /// Priority 0: Pre-computed pass ordinal (PassIndexOverride) — production-dominant path.
        ///              pass ordinal → Checkpoints[IndexForPass(pass)]
        ///              (Sequential clamps extra passes to last; Cyclic wraps.)
        ///
        /// Priority 1: Turnaround reference if participant has a turnaround reading.
        ///              Reading BEFORE turnaround → first checkpoint (pass 0)
        ///              Reading AFTER turnaround  → last checkpoint (pass N-1)
        ///              (For N=2 this is exactly the legacy outbound/return behavior.)
        ///
        /// Priority 2: Chronological order within shared device GROUP.
        ///              k-th reading across all devices in group → pass ordinal k-1.
        ///
        /// CRITICAL: Chronological ranking is per SHARED GROUP, not per device.
        /// Parent + child devices at the same location share a single rank counter.
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
            int turnaroundAssignments = 0, chronologicalAssignments = 0, singleDeviceAssignments = 0, passIndexAssignments = 0;

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

                    // Case 2: Shared device → map pass ordinal to the ordered checkpoint list
                    if (sharedDevices.TryGetValue(reading.DeviceId, out var mapping))
                    {
                        int pass;
                        string method;

                        if (reading.PassIndexOverride.HasValue)
                        {
                            // Priority 0: Pre-computed pass ordinal (pass-gap separated)
                            pass = reading.PassIndexOverride.Value;
                            method = "PassIndex";
                            passIndexAssignments++;
                        }
                        else if (hasTurnaround)
                        {
                            // Priority 1: Turnaround reference — before = first, after = last.
                            // For N=2 this is exactly the legacy outbound/return behavior.
                            pass = reading.ReadTimeUtc < participantTurnaround!.Value ? 0 : mapping.Count - 1;
                            method = "TurnaroundReference";
                            turnaroundAssignments++;
                        }
                        else
                        {
                            // Priority 2: Chronological rank within shared group (1-based → 0-based pass)
                            var rank = groupRanks.TryGetValue(reading.ReadingId, out var r) ? r : 1;
                            pass = rank - 1;
                            method = "ChronologicalOrder";
                            chronologicalAssignments++;
                        }

                        var slot = mapping.Checkpoints[mapping.IndexForPass(pass)];

                        results.Add(new AssignedReading
                        {
                            ReadingId = reading.ReadingId,
                            Epc = epc,
                            DeviceId = reading.DeviceId,
                            ReadTimeUtc = reading.ReadTimeUtc,
                            CheckpointId = slot.CheckpointId,
                            DistanceFromStart = slot.Distance,
                            CheckpointName = slot.Name,
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
                "Step 4: Assigned {Total} readings — PassIndex={PassIndex}, TurnaroundRef={Turnaround}, Chronological={Chrono}, SingleDevice={Single}",
                results.Count, passIndexAssignments, turnaroundAssignments, chronologicalAssignments, singleDeviceAssignments);

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