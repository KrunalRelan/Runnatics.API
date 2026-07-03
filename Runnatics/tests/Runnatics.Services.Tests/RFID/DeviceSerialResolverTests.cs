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

        // ------------------------------------------------------------------
        // ResolveDeviceId — the ONE priority rule (batch serial first, then the
        // read's own DeviceId), feeding the readings DTO's DeviceName column and
        // the assign-then-choose candidate resolution.
        // ------------------------------------------------------------------

        [TestMethod]
        public void ResolveDeviceId_KnownSerial_ResolvesToDeviceName()
        {
            var devices = new[] { D(7, "00162511ebf3", "box2"), D(9, "0016251182bc", "Box 01") };
            var lookup = DeviceSerialResolver.BuildLookup(devices);
            var byId = devices.ToDictionary(d => d.Id);

            var id = DeviceSerialResolver.ResolveDeviceId(lookup, "ebf3");
            Assert.AreEqual(7, id);
            Assert.AreEqual("box2", byId[id].Name);

            var id2 = DeviceSerialResolver.ResolveDeviceId(lookup, null, "Box 01");
            Assert.AreEqual(9, id2);
            Assert.AreEqual("Box 01", byId[id2].Name);
        }

        [TestMethod]
        public void ResolveDeviceId_BatchSerialWins_OverReadSerial()
        {
            var lookup = DeviceSerialResolver.BuildLookup(new[]
            {
                D(1, "00162511ebf3", "box2"),
                D(2, "0016251182bc", "Box 01")
            });

            // Batch serial (first arg) resolves → the read's own serial is never consulted.
            Assert.AreEqual(1, DeviceSerialResolver.ResolveDeviceId(lookup, "ebf3", "82bc"));
            // Batch serial unmapped/empty → falls through to the read's serial.
            Assert.AreEqual(2, DeviceSerialResolver.ResolveDeviceId(lookup, "ffff", "82bc"));
            Assert.AreEqual(2, DeviceSerialResolver.ResolveDeviceId(lookup, "", "82bc"));
        }

        [TestMethod]
        public void ResolveDeviceId_UnmappedSerial_ReturnsZero_SoNameFallsBackToSerial()
        {
            var lookup = DeviceSerialResolver.BuildLookup(new[] { D(1, "00162511ebf3", "box2") });

            // 0 = unresolved → the DTO's DeviceName stays null and the UI shows the raw serial.
            Assert.AreEqual(0, DeviceSerialResolver.ResolveDeviceId(lookup, "deadbeef", "cafe"));
            Assert.AreEqual(0, DeviceSerialResolver.ResolveDeviceId(lookup, null, null));
        }
    }
}
