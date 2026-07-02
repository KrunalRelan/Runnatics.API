using Microsoft.Extensions.Logging.Abstractions;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.RFID;
using static Runnatics.Services.RFID.LoopRaceCheckpointAssigner;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Suite section 3 — pass-collapse / shared start-finish mat (the race-65 class) against
    /// LoopRaceCheckpointAssigner.CollapseIntoPasses, plus section 2e (pass-collapse settings
    /// defaults). Fixture = race 65's active config and real timeline (UTC; IST − 5:30):
    ///
    ///   gun 00:03:00, EarlyStartCutOff 1s → floor 00:02:59, ceiling 00:23:00
    ///   Dev 2 (primary) → [396 start @0, 430 Finish @5]
    ///   Dev 1 (child of 2) → [429 @0, 431 @5]   (same shared group)
    ///   bib 5176: pre-gun cluster 00:01:22–00:02:37, real start 00:03:34,
    ///             finish 00:21:53 (Dev 1) / 00:21:54 (Dev 2)
    /// </summary>
    [TestClass]
    public class PassCollapseTests
    {
        private static readonly DateTime Gun = new(2026, 6, 29, 0, 3, 0, DateTimeKind.Utc);
        private static readonly DateTime Floor = Gun.AddSeconds(-1);
        private static readonly DateTime Ceiling = Gun.AddSeconds(1200);
        private const int Dedup = 30;
        private const int PassGap = 300;

        private LoopRaceCheckpointAssigner _assigner = null!;
        private Dictionary<int, SharedDeviceMapping> _shared = null!;
        private List<Checkpoint> _checkpoints = null!;

        [TestInitialize]
        public void Setup()
        {
            _assigner = new LoopRaceCheckpointAssigner(NullLogger.Instance);
            _checkpoints = new List<Checkpoint>
            {
                Cp(396, 2, 0m, "start"),
                Cp(429, 1, 0m, "start", parentDeviceId: 2),
                Cp(398, 11, 2.5m, "2.5 KM"),
                Cp(430, 2, 5m, "Finish"),
                Cp(431, 1, 5m, "Finish", parentDeviceId: 2)
            };
            _shared = _assigner.IdentifySharedDevices(_checkpoints);
        }

        private static Checkpoint Cp(int id, int deviceId, decimal distance, string name, int? parentDeviceId = null) =>
            new()
            {
                Id = id,
                EventId = 38,
                RaceId = 65,
                DeviceId = deviceId,
                ParentDeviceId = parentDeviceId,
                DistanceFromStart = distance,
                Name = name
            };

        private static ReadingInput Read(long id, int deviceId, DateTime time, string epc = "E1") =>
            new() { ReadingId = id, Epc = epc, DeviceId = deviceId, ReadTimeUtc = time };

        private PassCollapseResult Collapse(params ReadingInput[] reads) =>
            CollapseIntoPasses(reads, _shared, Floor, Ceiling, Dedup, PassGap);

        // ─── 3a: the bib-5176 case, end to end through collapse → assign → dedup ───

        [TestMethod]
        public void Bib5176_PreGunClusterMustNotSwallowRealStart_StartIs000334()
        {
            var reads = new[]
            {
                Read(1, 2, Gun.AddSeconds(-98)),   // 00:01:22 ┐
                Read(2, 2, Gun.AddSeconds(-60)),   // 00:02:00 ├ pre-gun cluster (57s before real start)
                Read(3, 2, Gun.AddSeconds(-23)),   // 00:02:37 ┘
                Read(4, 2, Gun.AddSeconds(34)),    // 00:03:34 — the REAL start (in-window)
                Read(5, 1, Gun.AddSeconds(1133)),  // 00:21:53 — finish (child mat)
                Read(6, 2, Gun.AddSeconds(1134))   // 00:21:54 — finish (primary mat, same pass)
            };

            var result = Collapse(reads);

            // The invariant: the collapse must NEVER merge reads across the floor.
            Assert.AreEqual(3, result.PreStartReadsExcluded, "all three pre-gun cluster reads excluded");
            var reps = result.ReadingsByEpc["E1"];
            Assert.AreEqual(2, reps.Count);
            Assert.AreEqual(Gun.AddSeconds(34), reps[0].ReadTimeUtc, "pass-0 representative = the real start");
            Assert.AreEqual(0, reps[0].PassIndexOverride);
            Assert.AreEqual(Gun.AddSeconds(1133), reps[1].ReadTimeUtc, "finish pass keeps EARLIEST (00:21:53)");
            Assert.AreEqual(1, reps[1].PassIndexOverride);

            // Chain through Step 4 + Step 5: start lands on CP 396, finish on the 5.0 KM slot.
            var assigned = _assigner.AssignAllCheckpoints(
                result.ReadingsByEpc, turnaroundConfig: null, _shared,
                new Dictionary<string, DateTime>(), medianTurnaround: null,
                new Dictionary<int, List<Checkpoint>>());
            var deduped = _assigner.DeduplicateAssignedReadings(assigned, _checkpoints);

            var start = deduped.Single(a => a.DistanceFromStart == 0m);
            var finish = deduped.Single(a => a.DistanceFromStart == 5m);
            Assert.AreEqual(Gun.AddSeconds(34), start.ReadTimeUtc, "bib 5176's start must be 05:33:34 IST");
            Assert.AreEqual(396, start.CheckpointId);
            Assert.AreEqual(Gun.AddSeconds(1133), finish.ReadTimeUtc);
            // Net sanity: ~18:19 — the plausible 5K, not 18:53 from a 05:51:53 "start".
            Assert.AreEqual(TimeSpan.FromSeconds(1099), finish.ReadTimeUtc - start.ReadTimeUtc);
        }

        // ─── 3b: in-window start cluster → EARLIEST wins (pinned representative) ───

        [TestMethod]
        public void InWindowStartCluster_RepresentativeIsEarliest()
        {
            var result = Collapse(
                Read(1, 2, Gun.AddSeconds(34)),   // :34 ← must win
                Read(2, 2, Gun.AddSeconds(36)),   // :36 (same pass, within dedup window)
                Read(3, 2, Gun.AddSeconds(40)));  // :40 (old keep-LAST would have picked this)

            var reps = result.ReadingsByEpc["E1"];
            Assert.AreEqual(1, reps.Count);
            Assert.AreEqual(Gun.AddSeconds(34), reps[0].ReadTimeUtc,
                "start = EARLIEST raw in-window read; keep-LAST must not displace the pinned start");
            Assert.AreEqual(0, reps[0].PassIndexOverride);
            Assert.AreEqual(0, result.PreStartReadsExcluded);
        }

        // ─── 3c: no valid start in group → placeholder survives (DNS path), keep-LAST intact ───

        [TestMethod]
        public void NoInWindowRead_PreFloorPlaceholderSurvivesWithKeepLast()
        {
            var result = Collapse(
                Read(1, 2, Gun.AddSeconds(-120)),   // 00:01:00
                Read(2, 2, Gun.AddSeconds(-110)));  // 00:01:10 (same crossing, dedup window)

            Assert.AreEqual(0, result.PreStartReadsExcluded, "no chosen start → nothing excluded");
            var reps = result.ReadingsByEpc["E1"];
            Assert.AreEqual(1, reps.Count);
            Assert.AreEqual(Gun.AddSeconds(-110), reps[0].ReadTimeUtc,
                "without a valid start the legacy keep-LAST applies (placeholder for Phase 3 DNS)");
            Assert.AreEqual(0, reps[0].PassIndexOverride,
                "placeholder takes ordinal 0 → start checkpoint → Phase 2/3 see the early read → DNS");
        }

        // ─── 3d: shared mat — post-ceiling finish read is never the start ───

        [TestMethod]
        public void PreFloorAndPostCeilingOnly_FinishReadNeverBecomesStart()
        {
            var result = Collapse(
                Read(1, 2, Gun.AddSeconds(-120)),    // pre-floor
                Read(2, 2, Gun.AddSeconds(1620)));   // 00:30:00 — post-ceiling (their finish)

            var reps = result.ReadingsByEpc["E1"];
            Assert.AreEqual(2, reps.Count);
            var postCeiling = reps.Single(r => r.ReadTimeUtc == Gun.AddSeconds(1620));
            Assert.AreEqual(1, postCeiling.PassIndexOverride,
                "the post-ceiling read maps to the finish slot (pass 1), never pass 0/start");
            var preFloor = reps.Single(r => r.ReadTimeUtc == Gun.AddSeconds(-120));
            Assert.AreEqual(0, preFloor.PassIndexOverride);
        }

        // ─── 3e: staggered event, one mat — cross-reads from another race's gun excluded ───

        [TestMethod]
        public void StaggeredRaces_CrossReadAtOtherGunExcluded_EvenAcrossUtcMidnight()
        {
            var result = Collapse(
                Read(1, 2, Gun.AddMinutes(-30)),     // 2026-06-28T23:33:00Z — the OTHER race's gun (cross-read)
                Read(2, 2, Gun.AddSeconds(10)),      // this runner's real start
                Read(3, 1, Gun.AddSeconds(1020)));   // their finish

            Assert.AreEqual(1, result.PreStartReadsExcluded, "cross-read at the other race's gun excluded");
            var reps = result.ReadingsByEpc["E1"];
            Assert.AreEqual(2, reps.Count);
            Assert.AreEqual(Gun.AddSeconds(10), reps[0].ReadTimeUtc);
            Assert.AreEqual(0, reps[0].PassIndexOverride);
            Assert.AreEqual(1, reps[1].PassIndexOverride);
        }

        // ─── Multi-EPC independence: one runner's chosen start never affects another ───

        [TestMethod]
        public void TwoRunners_StartSelectionIsPerEpc()
        {
            var result = Collapse(
                Read(1, 2, Gun.AddSeconds(-30), epc: "E1"),   // E1: pre-floor only → placeholder
                Read(2, 2, Gun.AddSeconds(20), epc: "E2"),    // E2: valid start
                Read(3, 1, Gun.AddSeconds(1100), epc: "E2")); // E2: finish

            Assert.AreEqual(Gun.AddSeconds(-30), result.ReadingsByEpc["E1"][0].ReadTimeUtc);
            Assert.AreEqual(Gun.AddSeconds(20), result.ReadingsByEpc["E2"][0].ReadTimeUtc);
            Assert.AreEqual(0, result.PreStartReadsExcluded);
        }

        // ─── Non-start shared groups are untouched by the gun window ───

        [TestMethod]
        public void MidCourseSharedGroup_NotStartBound_NoWindowFiltering()
        {
            // Device 7 shared across 2.5/7.5 KM — does NOT start at zero.
            var midCheckpoints = new List<Checkpoint>
            {
                Cp(500, 7, 2.5m, "2.5KM"),
                Cp(501, 7, 7.5m, "7.5KM")
            };
            var shared = _assigner.IdentifySharedDevices(midCheckpoints);

            var result = CollapseIntoPasses(
                new[] { Read(1, 7, Gun.AddSeconds(-600)), Read(2, 7, Gun.AddSeconds(2000)) },
                shared, Floor, Ceiling, Dedup, PassGap);

            Assert.AreEqual(0, result.PreStartReadsExcluded, "gun window applies to start-bound groups only");
            Assert.AreEqual(2, result.ReadingsByEpc["E1"].Count);
        }

        // ─── 2e: settings defaults / guards ───

        [TestMethod]
        public void PassCollapseSettings_NullZeroNegative_UseDefaults()
        {
            Assert.AreEqual(30, PassCollapseSettings.DedupSeconds(null));
            Assert.AreEqual(30, PassCollapseSettings.DedupSeconds(0));
            Assert.AreEqual(30, PassCollapseSettings.DedupSeconds(-1));
            Assert.AreEqual(45, PassCollapseSettings.DedupSeconds(45));

            Assert.AreEqual(300, PassCollapseSettings.PassGapSeconds(null));   // race 65: NULL → 300s
            Assert.AreEqual(300, PassCollapseSettings.PassGapSeconds(0));
            Assert.AreEqual(120, PassCollapseSettings.PassGapSeconds(120));
        }
    }
}
