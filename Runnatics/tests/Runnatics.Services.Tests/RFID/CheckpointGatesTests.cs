using Runnatics.Models.Data.Entities;
using Runnatics.Services.RFID;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Suite section 7d — deterministic start/finish gate selection. The prior
    /// OrderBy(DistanceFromStart).First() broke the same-distance tie (race 65: primary 396 vs
    /// child 429 at 0.0 KM) by DB return order; CheckpointGates must prefer the PRIMARY, in any
    /// input order, because normalization merges child readings INTO the primary.
    /// </summary>
    [TestClass]
    public class CheckpointGatesTests
    {
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

        private static List<Checkpoint> Race65() => new()
        {
            Cp(396, 2, 0m, "start"),
            Cp(429, 1, 0m, "start", parentDeviceId: 2),
            Cp(398, 11, 2.5m, "2.5 KM"),
            Cp(430, 2, 5m, "Finish"),
            Cp(431, 1, 5m, "Finish", parentDeviceId: 2)
        };

        [TestMethod]
        public void Start_SameDistanceTie_PrefersPrimaryOverChild()
        {
            Assert.AreEqual(396, CheckpointGates.Start(Race65())!.Id);
        }

        [TestMethod]
        public void Finish_SameDistanceTie_PrefersPrimaryOverChild()
        {
            Assert.AreEqual(430, CheckpointGates.Finish(Race65())!.Id);
        }

        [TestMethod]
        public void Selection_IsIndependentOfInputOrder()
        {
            // The child rows FIRST — the order a DB is free to return.
            var childFirst = new List<Checkpoint>
            {
                Cp(429, 1, 0m, "start", parentDeviceId: 2),
                Cp(431, 1, 5m, "Finish", parentDeviceId: 2),
                Cp(430, 2, 5m, "Finish"),
                Cp(396, 2, 0m, "start"),
                Cp(398, 11, 2.5m, "2.5 KM")
            };

            Assert.AreEqual(396, CheckpointGates.Start(childFirst)!.Id);
            Assert.AreEqual(430, CheckpointGates.Finish(childFirst)!.Id);
        }

        [TestMethod]
        public void EqualDistanceEqualRole_LowestIdWins_Deterministic()
        {
            // Legacy invalid data (two primaries at one distance — the validator now rejects it
            // at save/reprocess, but the selector must still be deterministic on old rows).
            var legacy = new List<Checkpoint>
            {
                Cp(430, 2, 5m, "Finish"),
                Cp(399, 1, 5m, "Finish"),
                Cp(396, 2, 0m, "start")
            };

            Assert.AreEqual(399, CheckpointGates.Finish(legacy)!.Id, "lowest Id among equal candidates");
        }

        [TestMethod]
        public void EmptyList_ReturnsNull()
        {
            Assert.IsNull(CheckpointGates.Start(new List<Checkpoint>()));
            Assert.IsNull(CheckpointGates.Finish(new List<Checkpoint>()));
        }

        // ────────────────────────────────────────────────────────────────────
        // RACE-66 INVARIANT: one logical gate (primary ∪ children), keyed by the
        // PRIMARY checkpoint id — CanonicalGateMap is the one child→parent fold every
        // consumer (Phase 2 merge, Phase 2.4 overrides, manual-time paths, readings
        // DTO) shares.
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Race 66's exact shape: Device 1 primary / Device 2 child at BOTH gates.</summary>
        private static List<Checkpoint> Race66() => new()
        {
            Cp(401, 1, 0m, "start"),
            Cp(402, 2, 0m, "", parentDeviceId: 1),
            Cp(404, 1, 10m, "Finish"),
            Cp(405, 2, 10m, "", parentDeviceId: 1)
        };

        [TestMethod]
        public void CanonicalGateMap_Race66Shape_ChildrenFoldOntoTheirPrimaries()
        {
            var map = CheckpointGates.CanonicalGateMap(Race66());

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(401, map[402]);
            Assert.AreEqual(404, map[405]);
        }

        [TestMethod]
        public void CanonicalGateMap_Race65MirrorShape_SameFold()
        {
            // Race 65 is the mirror image (Device 2 primary / Device 1 child) — the fold must be
            // shape-independent, not an artifact of which device id happens to be the parent.
            var map = CheckpointGates.CanonicalGateMap(Race65());

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(396, map[429]);
            Assert.AreEqual(430, map[431]);
        }

        [TestMethod]
        public void CanonicalGateMap_OrphanChild_GetsNoEntry()
        {
            // Parent device has no row at the child's distance — validator check (e) rejects the
            // config; the map must not invent a fold (absent = already canonical).
            var orphaned = new List<Checkpoint>
            {
                Cp(401, 1, 0m, "start"),
                Cp(402, 2, 5m, "", parentDeviceId: 1) // parent's rows are only at 0.0
            };

            var map = CheckpointGates.CanonicalGateMap(orphaned);

            Assert.AreEqual(0, map.Count);
        }

        [TestMethod]
        public void Canonical_PrimaryAndUnmappedIds_AreIdentity()
        {
            var map = CheckpointGates.CanonicalGateMap(Race66());

            Assert.AreEqual(401, CheckpointGates.Canonical(map, 401), "primary is identity");
            Assert.AreEqual(401, CheckpointGates.Canonical(map, 402), "child folds onto its primary");
            Assert.AreEqual(999, CheckpointGates.Canonical(map, 999), "unknown id is identity");
        }

        /// <summary>
        /// THE RACE-66 CROSS-DEVICE REPRO (bib 1002): start reads on the CHILD device (:49 →
        /// assigned to checkpoint 402) and the PRIMARY device 3s later (:52 → checkpoint 401).
        /// Folding both assignments through CanonicalGateMap yields ONE candidate set at the
        /// PRIMARY gate; the start gate's selection picks the LAST in-window read (:52), and the
        /// child checkpoint owns nothing. Without the fold they were two "gates" — two normalized
        /// rows at one physical start line.
        /// </summary>
        [TestMethod]
        public void CrossDeviceReadsAtOneGate_MergeToOneSelection_UnderPrimary_LastWins()
        {
            var checkpoints = Race66();
            var map = CheckpointGates.CanonicalGateMap(checkpoints);

            var gun = new DateTime(2026, 6, 28, 5, 30, 0, DateTimeKind.Utc);
            var childRead = (RawReadId: 83935L, CheckpointId: 402, Time: gun.AddSeconds(49));
            var primaryRead = (RawReadId: 83936L, CheckpointId: 401, Time: gun.AddSeconds(52));

            // Phase-2 semantics: assignments fold through the map BEFORE grouping into gates.
            var gates = new[] { childRead, primaryRead }
                .GroupBy(r => CheckpointGates.Canonical(map, r.CheckpointId))
                .Select(g => new GateInput
                {
                    GateId = g.Key,
                    IsStartGate = g.Key == CheckpointGates.Start(checkpoints)!.Id,
                    Candidates = g.OrderBy(r => r.Time)
                        .Select(r => new GateCandidate { Key = r.RawReadId, Time = r.Time })
                        .ToList()
                })
                .ToList();

            Assert.AreEqual(1, gates.Count, "one logical gate, not one per checkpoint id");
            Assert.AreEqual(401, gates[0].GateId, "the gate is the PRIMARY checkpoint");

            var chain = SequentialGateSelector.SelectChain(
                gates, gun.AddSeconds(-300), gun.AddSeconds(1200), passGapSeconds: 300, minSegmentSeconds: null);

            Assert.AreEqual(1, chain.Count);
            Assert.AreEqual(83936L, chain[401], "start = LAST read of the first in-window pass, across BOTH devices");
        }
    }
}
