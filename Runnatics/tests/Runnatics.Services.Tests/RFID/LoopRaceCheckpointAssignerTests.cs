using Microsoft.Extensions.Logging.Abstractions;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.RFID;
using static Runnatics.Services.RFID.LoopRaceCheckpointAssigner;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// ISSUE-1 (N-checkpoint assignment) test suite.
    ///
    /// Covers:
    ///   1. SharedDeviceMapping.IndexForPass — Sequential clamp + Cyclic wrap + edge cases
    ///   2. IdentifySharedDevices — N=1/2/3/4 device groups + paired parent/child devices
    ///   3. Topology B regression fixture — out-and-back (N=2) produces the exact legacy
    ///      outbound/return assignments via PassIndex, TurnaroundReference and
    ///      ChronologicalOrder paths
    ///   4. Topology C — point-to-point device reuse at 3 locations (7th GGHM shape)
    ///   5. Cyclic mode — pass % N wrap-around
    /// </summary>
    [TestClass]
    public class LoopRaceCheckpointAssignerTests
    {
        private LoopRaceCheckpointAssigner _assigner = null!;

        [TestInitialize]
        public void Setup()
        {
            _assigner = new LoopRaceCheckpointAssigner(NullLogger.Instance);
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private static Checkpoint Cp(int id, int deviceId, decimal distance, string name, int? parentDeviceId = null) =>
            new()
            {
                Id = id,
                EventId = 1,
                RaceId = 1,
                DeviceId = deviceId,
                ParentDeviceId = parentDeviceId,
                DistanceFromStart = distance,
                Name = name
            };

        private static ReadingInput Read(long id, string epc, int deviceId, DateTime time, int? passOverride = null) =>
            new()
            {
                ReadingId = id,
                Epc = epc,
                DeviceId = deviceId,
                ReadTimeUtc = time,
                PassIndexOverride = passOverride
            };

        private static SharedDeviceMapping Mapping(AssignmentMode mode, params (int id, decimal dist, string name)[] slots) =>
            new()
            {
                DeviceId = 1,
                Mode = mode,
                SharedGroupKey = "G",
                Checkpoints = slots
                    .Select(s => new CheckpointSlot { CheckpointId = s.id, Distance = s.dist, Name = s.name })
                    .ToList()
            };

        private static readonly DateTime T0 = new(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc);

        // ─────────────────────────────────────────────────────────────────
        // 1. IndexForPass
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void IndexForPass_Sequential_N2_MapsAndClampsLikeLegacyOutboundReturn()
        {
            var m = Mapping(AssignmentMode.Sequential, (101, 0m, "Start"), (102, 21.1m, "Finish"));

            Assert.AreEqual(0, m.IndexForPass(0));   // pass 0 → outbound
            Assert.AreEqual(1, m.IndexForPass(1));   // pass 1 → return
            Assert.AreEqual(1, m.IndexForPass(2));   // extra pass clamps to last (legacy "1+ = return")
            Assert.AreEqual(1, m.IndexForPass(99));
        }

        [TestMethod]
        public void IndexForPass_Sequential_N3_MapsOrdinalsAndClamps()
        {
            var m = Mapping(AssignmentMode.Sequential, (1, 0m, "Start"), (2, 10.5m, "10.5KM"), (3, 21.1m, "Finish"));

            Assert.AreEqual(0, m.IndexForPass(0));
            Assert.AreEqual(1, m.IndexForPass(1));
            Assert.AreEqual(2, m.IndexForPass(2));
            Assert.AreEqual(2, m.IndexForPass(3));   // loiter/re-cross → clamp to Finish
        }

        [TestMethod]
        public void IndexForPass_Cyclic_WrapsModuloN()
        {
            var m = Mapping(AssignmentMode.Cyclic, (1, 0m, "Start"), (2, 5m, "5KM"));

            Assert.AreEqual(0, m.IndexForPass(0));
            Assert.AreEqual(1, m.IndexForPass(1));
            Assert.AreEqual(0, m.IndexForPass(2));   // lap 2 outbound
            Assert.AreEqual(1, m.IndexForPass(3));   // lap 2 return
            Assert.AreEqual(0, m.IndexForPass(4));
        }

        [TestMethod]
        public void IndexForPass_EdgeCases_NegativeAndEmptyAreSafe()
        {
            var seq = Mapping(AssignmentMode.Sequential, (1, 0m, "Start"), (2, 5m, "5KM"));
            var cyc = Mapping(AssignmentMode.Cyclic, (1, 0m, "Start"), (2, 5m, "5KM"));

            Assert.AreEqual(0, seq.IndexForPass(-1));            // defensive clamp low
            Assert.AreEqual(1, cyc.IndexForPass(-1));            // safe modulo, no exception
            Assert.AreEqual(0, new SharedDeviceMapping().IndexForPass(5)); // empty list → 0, no throw
        }

        [TestMethod]
        public void IndexForPass_SingleCheckpoint_AlwaysZero()
        {
            var seq = Mapping(AssignmentMode.Sequential, (7, 2.5m, "Turn"));
            var cyc = Mapping(AssignmentMode.Cyclic, (7, 2.5m, "Turn"));

            for (int p = 0; p < 5; p++)
            {
                Assert.AreEqual(0, seq.IndexForPass(p));
                Assert.AreEqual(0, cyc.IndexForPass(p));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. IdentifySharedDevices
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void IdentifySharedDevices_N1_IsNotShared()
        {
            var checkpoints = new List<Checkpoint> { Cp(1, 10, 10.55m, "Turnaround") };

            var shared = _assigner.IdentifySharedDevices(checkpoints);

            Assert.AreEqual(0, shared.Count);
        }

        [TestMethod]
        public void IdentifySharedDevices_N2_OrderedByDistance()
        {
            // Deliberately list Finish first — ordering must come from distance, not input order.
            var checkpoints = new List<Checkpoint>
            {
                Cp(2, 10, 21.1m, "Finish"),
                Cp(1, 10, 0m, "Start")
            };

            var shared = _assigner.IdentifySharedDevices(checkpoints);

            Assert.AreEqual(1, shared.Count);
            var m = shared[10];
            Assert.AreEqual(2, m.Count);
            Assert.AreEqual(1, m.Checkpoints[0].CheckpointId);   // Start (0km) first
            Assert.AreEqual(2, m.Checkpoints[1].CheckpointId);   // Finish (21.1km) second
            Assert.IsTrue(m.StartsAtZero);
            Assert.AreEqual("Start_Finish", m.SharedGroupKey);
        }

        [TestMethod]
        public void IdentifySharedDevices_N3_BuildsFullOrderedList()
        {
            // 7th GGHM Box-1 shape: 0 / 10.5 / 21.1
            var checkpoints = new List<Checkpoint>
            {
                Cp(3, 1, 21.1m, "Finish"),
                Cp(1, 1, 0m, "Start"),
                Cp(2, 1, 10.5m, "10.5KM")
            };

            var shared = _assigner.IdentifySharedDevices(checkpoints);

            Assert.AreEqual(1, shared.Count);
            var m = shared[1];
            Assert.AreEqual(3, m.Count);
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                m.Checkpoints.Select(s => s.CheckpointId).ToArray());
            CollectionAssert.AreEqual(
                new[] { 0m, 10.5m, 21.1m },
                m.Checkpoints.Select(s => s.Distance).ToArray());
            Assert.AreEqual("Start_10.5KM_Finish", m.SharedGroupKey);
        }

        [TestMethod]
        public void IdentifySharedDevices_N4_BuildsFullOrderedList()
        {
            // 7th GGHM Box-6 shape: 2.5 / 7.5 / 13 / 18.5
            var checkpoints = new List<Checkpoint>
            {
                Cp(4, 15, 18.5m, "18.5KM"),
                Cp(2, 15, 7.5m, "7.5KM"),
                Cp(1, 15, 2.5m, "2.5KM"),
                Cp(3, 15, 13m, "13KM")
            };

            var shared = _assigner.IdentifySharedDevices(checkpoints);

            var m = shared[15];
            Assert.AreEqual(4, m.Count);
            CollectionAssert.AreEqual(
                new[] { 2.5m, 7.5m, 13m, 18.5m },
                m.Checkpoints.Select(s => s.Distance).ToArray());
            Assert.IsFalse(m.StartsAtZero);
        }

        [TestMethod]
        public void IdentifySharedDevices_PairedChildDevices_ResolveIdentically()
        {
            // Parent device 11 + child device 12 at the SAME two locations.
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 11, 0m, "Start"),
                Cp(2, 11, 21.1m, "Finish"),
                Cp(3, 12, 0m, "Start", parentDeviceId: 11),
                Cp(4, 12, 21.1m, "Finish", parentDeviceId: 11)
            };

            var shared = _assigner.IdentifySharedDevices(checkpoints);

            Assert.AreEqual(2, shared.Count);
            var parent = shared[11];
            var child = shared[12];

            // Child inherits the parent's group key → single shared rank/pass counter.
            Assert.AreEqual(parent.SharedGroupKey, child.SharedGroupKey);

            // Same distance ordering on both → a shared pass ordinal selects the
            // same-distance slot on either device.
            CollectionAssert.AreEqual(
                parent.Checkpoints.Select(s => s.Distance).ToArray(),
                child.Checkpoints.Select(s => s.Distance).ToArray());
        }

        [TestMethod]
        public void IdentifySharedDevices_ModeIsStamped()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 10, 0m, "Start"),
                Cp(2, 10, 5m, "5KM")
            };

            Assert.AreEqual(AssignmentMode.Sequential,
                _assigner.IdentifySharedDevices(checkpoints, AssignmentMode.Sequential)[10].Mode);
            Assert.AreEqual(AssignmentMode.Cyclic,
                _assigner.IdentifySharedDevices(checkpoints, AssignmentMode.Cyclic)[10].Mode);
        }

        [TestMethod]
        public void IdentifySharedDevices_MixedCounts_OnlySharedDevicesReturned()
        {
            // Topology F slice: device 1 single, device 2 → N=2, device 3 → N=3
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 1, 10.55m, "Turnaround"),
                Cp(2, 2, 0m, "Start"), Cp(3, 2, 21.1m, "Finish"),
                Cp(4, 3, 5m, "5KM"), Cp(5, 3, 10m, "10KM"), Cp(6, 3, 16m, "16KM")
            };

            var shared = _assigner.IdentifySharedDevices(checkpoints);

            Assert.AreEqual(2, shared.Count);
            Assert.IsFalse(shared.ContainsKey(1));   // single device is NOT shared
            Assert.AreEqual(2, shared[2].Count);
            Assert.AreEqual(3, shared[3].Count);
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. Topology B regression fixture (out-and-back, N=2 + turnaround)
        //    Must reproduce the legacy outbound/return assignments exactly.
        // ─────────────────────────────────────────────────────────────────

        private (Dictionary<int, SharedDeviceMapping> shared, TurnaroundConfig turnaround, List<Checkpoint> checkpoints)
            BuildTopologyB()
        {
            // Republic Day shape: Box-16 (dev 16) Start/Finish, Box-19 (dev 19) 5KM/16.1KM,
            // Box-15 (dev 15) turnaround at 10.55KM.
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 16, 0m, "Start"),
                Cp(2, 19, 5m, "5KM"),
                Cp(3, 15, 10.55m, "Turnaround"),
                Cp(4, 19, 16.1m, "16.1KM"),
                Cp(5, 16, 21.1m, "Finish")
            };

            var shared = _assigner.IdentifySharedDevices(checkpoints, AssignmentMode.Sequential);
            var turnaround = _assigner.IdentifyTurnaroundCheckpoint(checkpoints)!;
            return (shared, turnaround, checkpoints);
        }

        [TestMethod]
        public void TopologyB_PassIndexPath_ProducesLegacyOutboundReturn()
        {
            var (shared, turnaround, _) = BuildTopologyB();

            // Runner: start mat (pass 0), 5KM out (pass 0), turnaround, 16.1KM back (pass 1), finish (pass 1)
            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 16, T0,                 passOverride: 0),   // Start
                    Read(2, "EPC1", 19, T0.AddMinutes(28),  passOverride: 0),   // 5KM (outbound)
                    Read(3, "EPC1", 15, T0.AddMinutes(60)),                     // Turnaround (single device)
                    Read(4, "EPC1", 19, T0.AddMinutes(92),  passOverride: 1),   // 16.1KM (return)
                    Read(5, "EPC1", 16, T0.AddMinutes(120), passOverride: 1)    // Finish
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, turnaround, shared,
                turnaroundTimes: new Dictionary<string, DateTime> { ["EPC1"] = T0.AddMinutes(60) },
                medianTurnaround: T0.AddMinutes(60),
                singleDeviceCheckpoints: new Dictionary<int, List<Checkpoint>>());

            Assert.AreEqual(5, assigned.Count);
            var byReading = assigned.ToDictionary(a => a.ReadingId, a => a.CheckpointId);
            Assert.AreEqual(1, byReading[1]);   // Start    (legacy: outbound)
            Assert.AreEqual(2, byReading[2]);   // 5KM      (legacy: outbound)
            Assert.AreEqual(3, byReading[3]);   // Turnaround
            Assert.AreEqual(4, byReading[4]);   // 16.1KM   (legacy: return)
            Assert.AreEqual(5, byReading[5]);   // Finish   (legacy: return)
            Assert.IsTrue(assigned.Where(a => a.ReadingId != 3).All(a => a.AssignmentMethod == "PassIndex"));
        }

        [TestMethod]
        public void TopologyB_ExtraPass_ClampsToReturnLikeLegacy()
        {
            var (shared, turnaround, _) = BuildTopologyB();

            // 3rd pass on the Start/Finish device (loiter/re-cross) — legacy mapped 1+ to return.
            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 16, T0, passOverride: 0),
                    Read(2, "EPC1", 16, T0.AddMinutes(120), passOverride: 1),
                    Read(3, "EPC1", 16, T0.AddMinutes(140), passOverride: 2)   // extra
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, turnaround, shared,
                new Dictionary<string, DateTime>(), null,
                new Dictionary<int, List<Checkpoint>>());

            var byReading = assigned.ToDictionary(a => a.ReadingId, a => a.CheckpointId);
            Assert.AreEqual(1, byReading[1]);   // Start
            Assert.AreEqual(5, byReading[2]);   // Finish
            Assert.AreEqual(5, byReading[3]);   // extra pass → clamped to Finish (= legacy return)
        }

        [TestMethod]
        public void TopologyB_TurnaroundFallback_NoOverride_BeforeAfterMapsFirstLast()
        {
            var (shared, turnaround, _) = BuildTopologyB();

            // No PassIndexOverride → Priority 1 turnaround reference must decide.
            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 19, T0.AddMinutes(28)),   // before turnaround → 5KM
                    Read(2, "EPC1", 19, T0.AddMinutes(92))    // after turnaround → 16.1KM
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, turnaround, shared,
                turnaroundTimes: new Dictionary<string, DateTime> { ["EPC1"] = T0.AddMinutes(60) },
                medianTurnaround: null,
                singleDeviceCheckpoints: new Dictionary<int, List<Checkpoint>>());

            var byReading = assigned.ToDictionary(a => a.ReadingId, a => a.CheckpointId);
            Assert.AreEqual(2, byReading[1]);   // 5KM
            Assert.AreEqual(4, byReading[2]);   // 16.1KM
            Assert.IsTrue(assigned.All(a => a.AssignmentMethod == "TurnaroundReference"));
        }

        [TestMethod]
        public void TopologyB_ChronologicalFallback_NoOverrideNoTurnaround_RankMapsToOrdinal()
        {
            var (shared, _, _) = BuildTopologyB();

            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 16, T0),                  // group rank 1 → Start
                    Read(2, "EPC1", 16, T0.AddMinutes(120))   // group rank 2 → Finish
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, turnaroundConfig: null, shared,
                new Dictionary<string, DateTime>(), medianTurnaround: null,
                new Dictionary<int, List<Checkpoint>>());

            var byReading = assigned.ToDictionary(a => a.ReadingId, a => a.CheckpointId);
            Assert.AreEqual(1, byReading[1]);
            Assert.AreEqual(5, byReading[2]);
            Assert.IsTrue(assigned.All(a => a.AssignmentMethod == "ChronologicalOrder"));
        }

        // ─────────────────────────────────────────────────────────────────
        // 4. Topology C — point-to-point, device at 3 locations (7th GGHM)
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void TopologyC_N3Sequential_PassOrdinalsMapToDistanceOrder()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 1, 0m, "Start"),
                Cp(2, 1, 10.5m, "10.5KM"),
                Cp(3, 1, 21.1m, "Finish")
            };
            var shared = _assigner.IdentifySharedDevices(checkpoints, AssignmentMode.Sequential);

            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 1, T0,                 passOverride: 0),
                    Read(2, "EPC1", 1, T0.AddMinutes(63),  passOverride: 1),
                    Read(3, "EPC1", 1, T0.AddMinutes(127), passOverride: 2),
                    Read(4, "EPC1", 1, T0.AddMinutes(150), passOverride: 3)   // re-cross after finishing
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, turnaroundConfig: null, shared,
                new Dictionary<string, DateTime>(), null,
                new Dictionary<int, List<Checkpoint>>());

            Assert.AreEqual(4, assigned.Count, "3-location device readings must NOT be dropped (the original ISSUE-1 bug)");
            var byReading = assigned.ToDictionary(a => a.ReadingId, a => a.CheckpointId);
            Assert.AreEqual(1, byReading[1]);   // pass 0 → 0km
            Assert.AreEqual(2, byReading[2]);   // pass 1 → 10.5km
            Assert.AreEqual(3, byReading[3]);   // pass 2 → 21.1km
            Assert.AreEqual(3, byReading[4]);   // extra → clamp to Finish
        }

        [TestMethod]
        public void TopologyC_FewerPassesThanCheckpoints_OnlyExistingPassesAssigned()
        {
            // EDGE-3 (same-device DNF shape): runner only produced 2 passes on an N=3 device.
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 1, 0m, "Start"),
                Cp(2, 1, 10.5m, "10.5KM"),
                Cp(3, 1, 21.1m, "Finish")
            };
            var shared = _assigner.IdentifySharedDevices(checkpoints, AssignmentMode.Sequential);

            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 1, T0, passOverride: 0),
                    Read(2, "EPC1", 1, T0.AddMinutes(63), passOverride: 1)
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, null, shared,
                new Dictionary<string, DateTime>(), null,
                new Dictionary<int, List<Checkpoint>>());

            Assert.AreEqual(2, assigned.Count);
            Assert.IsFalse(assigned.Any(a => a.CheckpointId == 3), "Unreached checkpoint must get no reading");
        }

        // ─────────────────────────────────────────────────────────────────
        // 5. Cyclic mode — loop with reused checkpoint rows
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Cyclic_N2TwoLaps_PassesWrapAroundCheckpoints()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 1, 0m, "Start"),
                Cp(2, 1, 5m, "5KM")
            };
            var shared = _assigner.IdentifySharedDevices(checkpoints, AssignmentMode.Cyclic);

            var readings = new Dictionary<string, List<ReadingInput>>
            {
                ["EPC1"] = new()
                {
                    Read(1, "EPC1", 1, T0,                passOverride: 0),
                    Read(2, "EPC1", 1, T0.AddMinutes(25), passOverride: 1),
                    Read(3, "EPC1", 1, T0.AddMinutes(50), passOverride: 2),   // lap 2 → wraps to Start
                    Read(4, "EPC1", 1, T0.AddMinutes(75), passOverride: 3)    // lap 2 → wraps to 5KM
                }
            };

            var assigned = _assigner.AssignAllCheckpoints(
                readings, null, shared,
                new Dictionary<string, DateTime>(), null,
                new Dictionary<int, List<Checkpoint>>());

            var byReading = assigned.ToDictionary(a => a.ReadingId, a => a.CheckpointId);
            Assert.AreEqual(1, byReading[1]);
            Assert.AreEqual(2, byReading[2]);
            Assert.AreEqual(1, byReading[3]);   // wrap
            Assert.AreEqual(2, byReading[4]);   // wrap
        }
    }
}
