using Microsoft.Extensions.Logging.Abstractions;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.RFID;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// CheckpointConfigValidator test suite (race-65 hardening).
    ///
    /// Fixtures mirror the two real race-65 states:
    ///   - the ACTIVE 5-row config (clean shared start/finish mat) must validate;
    ///   - the historical 8-row state (with the since-deleted rows) must fire
    ///     (a) duplicate primaries, (b) circular parent/child, (c) contradictory
    ///     roles and (d) same-device equal distances.
    /// Also covers the LoopRaceCheckpointAssigner promotion: same-device equal
    /// distances now hard-fail instead of warning.
    /// </summary>
    [TestClass]
    public class CheckpointConfigValidatorTests
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

        // ─────────────────────────────────────────────────────────────────
        // Clean configs → no violations
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Validate_Race65ActiveConfig_SharedMatWithChild_IsValid()
        {
            // The ACTIVE race-65 shape: Dev 2 primary + Dev 1 child at 0.0 and 5.0, Dev 11 at 2.5.
            var checkpoints = new List<Checkpoint>
            {
                Cp(396, 2, 0m, "start"),
                Cp(429, 1, 0m, "start", parentDeviceId: 2),
                Cp(398, 11, 2.5m, "2.5 KM"),
                Cp(430, 2, 5m, "Finish"),
                Cp(431, 1, 5m, "Finish", parentDeviceId: 2)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.AreEqual(0, violations.Count,
                "Clean shared start/finish mat (primary + child) must validate: " + string.Join(" | ", violations));
        }

        [TestMethod]
        public void Validate_PointToPointSharedDevice_IsValid()
        {
            // 7th GGHM shape: one device reused at 3 DIFFERENT distances is legitimate.
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 1, 0m, "Start"),
                Cp(2, 1, 10.5m, "10.5KM"),
                Cp(3, 1, 21.1m, "Finish"),
                Cp(4, 5, 5m, "5KM")
            };

            Assert.AreEqual(0, CheckpointConfigValidator.Validate(checkpoints).Count);
        }

        [TestMethod]
        public void Validate_EmptyConfig_IsValid()
        {
            Assert.AreEqual(0, CheckpointConfigValidator.Validate(new List<Checkpoint>()).Count);
        }

        // ─────────────────────────────────────────────────────────────────
        // (a) Duplicate primary checkpoints at the same distance
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Validate_DuplicatePrimariesAtSameDistance_Fires()
        {
            // The deleted race-65 state: TWO primary Finish rows at 5.0 (Dev 1 + Dev 2).
            var checkpoints = new List<Checkpoint>
            {
                Cp(396, 2, 0m, "start"),
                Cp(398, 11, 2.5m, "2.5 KM"),
                Cp(399, 1, 5m, "Finish"),
                Cp(430, 2, 5m, "Finish")
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains(violations[0], "Duplicate PRIMARY checkpoints at 5");
            StringAssert.Contains(violations[0], "399");
            StringAssert.Contains(violations[0], "430");
        }

        // ─────────────────────────────────────────────────────────────────
        // (b) Circular parent/child device references
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Validate_CircularParentChild_Fires()
        {
            // Deleted race-65 rows: Dev 2 child of Dev 1 AND Dev 1 child of Dev 2.
            var checkpoints = new List<Checkpoint>
            {
                Cp(397, 2, 0m, "start", parentDeviceId: 1),
                Cp(429, 1, 0m, "start", parentDeviceId: 2)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.IsTrue(violations.Any(v => v.Contains("Circular parent/child")),
                "Expected a circular-reference violation: " + string.Join(" | ", violations));
            // The 2-cycle must be reported exactly once, not once per direction.
            Assert.AreEqual(1, violations.Count(v => v.Contains("Circular parent/child")));
        }

        [TestMethod]
        public void Validate_SelfParent_Fires()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 3, 0m, "Start", parentDeviceId: 3)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.IsTrue(violations.Any(v => v.Contains("its own parent")),
                string.Join(" | ", violations));
        }

        [TestMethod]
        public void Validate_CycleBehindNonCyclicPrefix_Fires()
        {
            // 5 → 1 → 2 → 1: the walk has a non-cyclic prefix; the reported cycle is 1 → 2.
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 5, 0m, "Start", parentDeviceId: 1),
                Cp(2, 1, 0m, "Start", parentDeviceId: 2),
                Cp(3, 2, 0m, "Start", parentDeviceId: 1)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);
            var cycle = violations.FirstOrDefault(v => v.Contains("Circular parent/child"));

            Assert.IsNotNull(cycle, string.Join(" | ", violations));
            StringAssert.Contains(cycle, "1 → 2 → 1");
            Assert.IsFalse(cycle.Contains("5"), "The non-cyclic prefix device must not be reported as part of the cycle.");
        }

        // ─────────────────────────────────────────────────────────────────
        // (c) Contradictory device roles
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Validate_DeviceBothPrimaryAndChild_Fires()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(396, 2, 0m, "start"),                       // Dev 2 primary
                Cp(400, 2, 5m, "Finish", parentDeviceId: 1),   // Dev 2 child
                Cp(399, 1, 5m, "Finish")                       // Dev 1 primary
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.IsTrue(violations.Any(v => v.Contains("Device 2 is PRIMARY in one checkpoint row and a CHILD in another")),
                string.Join(" | ", violations));
        }

        [TestMethod]
        public void Validate_ChildOfMultipleParents_Fires()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 1, 0m, "Start"),
                Cp(2, 2, 5m, "5KM"),
                Cp(3, 3, 0m, "Start", parentDeviceId: 1),
                Cp(4, 3, 5m, "5KM", parentDeviceId: 2)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.IsTrue(violations.Any(v => v.Contains("Device 3 is a child of MULTIPLE parents")),
                string.Join(" | ", violations));
        }

        // ─────────────────────────────────────────────────────────────────
        // (d) Same device, same distance
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Validate_SameDeviceSameDistance_Fires()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(396, 2, 0m, "start"),
                Cp(397, 2, 0m, "start B", parentDeviceId: 1)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.IsTrue(violations.Any(v => v.Contains("Device 2 has 2 checkpoint rows at the same distance")),
                string.Join(" | ", violations));
        }

        // ─────────────────────────────────────────────────────────────────
        // The full historical race-65 8-row state → a, b, c and d all fire
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Validate_Race65HistoricalEightRowState_FiresAllCheckCategories()
        {
            var checkpoints = new List<Checkpoint>
            {
                Cp(396, 2, 0m, "start"),
                Cp(397, 2, 0m, "start", parentDeviceId: 1),
                Cp(429, 1, 0m, "start", parentDeviceId: 2),
                Cp(398, 11, 2.5m, "2.5 KM"),
                Cp(399, 1, 5m, "Finish"),
                Cp(430, 2, 5m, "Finish"),
                Cp(400, 2, 5m, "Finish", parentDeviceId: 1),
                Cp(431, 1, 5m, "Finish", parentDeviceId: 2)
            };

            var violations = CheckpointConfigValidator.Validate(checkpoints);

            Assert.IsTrue(violations.Any(v => v.Contains("Duplicate PRIMARY checkpoints at 5")), "(a) missing: " + string.Join(" | ", violations));
            Assert.IsTrue(violations.Any(v => v.Contains("Circular parent/child")), "(b) missing: " + string.Join(" | ", violations));
            Assert.IsTrue(violations.Any(v => v.Contains("Device 1 is PRIMARY in one checkpoint row and a CHILD in another")), "(c) Dev 1 missing");
            Assert.IsTrue(violations.Any(v => v.Contains("Device 2 is PRIMARY in one checkpoint row and a CHILD in another")), "(c) Dev 2 missing");
            Assert.IsTrue(violations.Any(v => v.Contains("at the same distance")), "(d) missing: " + string.Join(" | ", violations));
        }

        // ─────────────────────────────────────────────────────────────────
        // LoopRaceCheckpointAssigner promotion: equal distances hard-fail
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void IdentifySharedDevices_SameDeviceSameDistance_Throws()
        {
            var assigner = new LoopRaceCheckpointAssigner(NullLogger.Instance);
            var checkpoints = new List<Checkpoint>
            {
                Cp(1, 2, 0m, "start"),
                Cp(2, 2, 0m, "start B")
            };

            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => assigner.IdentifySharedDevices(checkpoints));
            StringAssert.Contains(ex.Message, "same");
            StringAssert.Contains(ex.Message, "device 2");
        }
    }
}
