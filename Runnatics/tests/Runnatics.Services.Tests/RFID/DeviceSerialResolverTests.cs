using Runnatics.Models.Data.Entities;
using Runnatics.Services.RFID;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// DeviceSerialResolver — the ONE serial→device map (extracted from Phase 1.5 FIX #2/#9),
    /// now also feeding assign-then-choose. Pins the variant registrations and the
    /// most-specific-wins TryAdd semantics so resolution can never silently change.
    /// </summary>
    [TestClass]
    public class DeviceSerialResolverTests
    {
        private static Device D(int id, string? mac, string? name = null) =>
            new() { Id = id, DeviceMacAddress = mac ?? string.Empty, Name = name ?? string.Empty };

        [TestMethod]
        public void FullMac_StrippedMac_AndSuffixVariants_AllResolve()
        {
            var lookup = DeviceSerialResolver.BuildLookup(new[] { D(1, "00:16:25:11:eb:f3") });

            Assert.AreEqual(1, lookup["00:16:25:11:eb:f3"]);   // full with separators
            Assert.AreEqual(1, lookup["00162511ebf3"]);        // stripped
            Assert.AreEqual(1, lookup["11ebf3"]);              // last-6
            Assert.AreEqual(1, lookup["ebf3"]);                // last-4 (UploadBatches.DeviceId form)
        }

        [TestMethod]
        public void NameVariants_Resolve_WithAndWithoutSpaces()
        {
            var lookup = DeviceSerialResolver.BuildLookup(new[] { D(12, "0016251182bc", "Box 16") });

            Assert.AreEqual(12, lookup["Box 16"]);
            Assert.AreEqual(12, lookup["Box16"]);
        }

        [TestMethod]
        public void Lookup_IsCaseInsensitive()
        {
            var lookup = DeviceSerialResolver.BuildLookup(new[] { D(1, "00162511EBF3", "Box 16") });

            Assert.AreEqual(1, lookup["00162511ebf3"]);
            Assert.AreEqual(1, lookup["BOX 16"]);
        }

        [TestMethod]
        public void SuffixCollision_FirstMostSpecificRegistrationWins()
        {
            // Two devices sharing the last-4 suffix: TryAdd keeps the first registration —
            // exact/full-MAC lookups stay unambiguous either way.
            var lookup = DeviceSerialResolver.BuildLookup(new[]
            {
                D(1, "00162511ebf3"),
                D(2, "99999999ebf3")
            });

            Assert.AreEqual(1, lookup["ebf3"], "first registration wins the shared suffix");
            Assert.AreEqual(1, lookup["00162511ebf3"]);
            Assert.AreEqual(2, lookup["99999999ebf3"], "full MACs never collide");
        }

        [TestMethod]
        public void MissingMacOrName_SkippedGracefully()
        {
            var lookup = DeviceSerialResolver.BuildLookup(new[] { D(1, null, null), D(2, "0016251182bc") });

            Assert.IsFalse(lookup.ContainsKey(string.Empty));
            Assert.AreEqual(2, lookup["0016251182bc"]);
        }
    }
}
