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
    }
}
