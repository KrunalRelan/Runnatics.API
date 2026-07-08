using Runnatics.Services;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Phase 2.45 derived-time math (DerivedTimes) — THE frozen-net regression pins
    /// (bib 1002, 2026-07-07): a changed START crossing must move every downstream
    /// row's NetTime; two different starts can never produce byte-identical net.
    /// </summary>
    [TestClass]
    public class DerivedTimesTests
    {
        // Race 66's shape: gun 05:30:00 IST = 00:00:00 UTC on 2026-06-28.
        private static readonly DateTime Gun = new(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc);

        // ─── NetBaseline: the four start states ───

        [TestMethod]
        public void NetBaseline_NoStartRows_IsNull()
        {
            Assert.IsNull(DerivedTimes.NetBaseline(Gun, hasStartRows: false, selectedInWindowStartChip: null));
        }

        [TestMethod]
        public void NetBaseline_StartRowsButNoneInWindow_FallsBackToGun()
        {
            Assert.AreEqual(Gun, DerivedTimes.NetBaseline(Gun, hasStartRows: true, selectedInWindowStartChip: null));
        }

        [TestMethod]
        public void NetBaseline_InWindowPostGunStart_IsTheCrossing()
        {
            var chip = Gun.AddSeconds(52.487);
            Assert.AreEqual(chip, DerivedTimes.NetBaseline(Gun, hasStartRows: true, selectedInWindowStartChip: chip));
        }

        [TestMethod]
        public void NetBaseline_InWindowPreGunStart_GunClamped()
        {
            // BUG-27: an early (pre-gun, in-window) start nets from the gun, never negative.
            var chip = Gun.AddSeconds(-10);
            Assert.AreEqual(Gun, DerivedTimes.NetBaseline(Gun, hasStartRows: true, selectedInWindowStartChip: chip));
        }

        // ─── ForRow: per-row Gun/Net rules ───

        [TestMethod]
        public void ForRow_StartGateRow_NetEqualsGunOffset()
        {
            var chip = Gun.AddSeconds(49.679);
            var (gunMs, netMs) = DerivedTimes.ForRow(chip, Gun, baseline: chip, isStartGateRow: true);

            Assert.AreEqual(49679L, gunMs);
            Assert.AreEqual(49679L, netMs);
        }

        [TestMethod]
        public void ForRow_NullBaseline_NetIsNull()
        {
            var (gunMs, netMs) = DerivedTimes.ForRow(Gun.AddMinutes(30), Gun, baseline: null, isStartGateRow: false);

            Assert.AreEqual(1_800_000L, gunMs);
            Assert.IsNull(netMs);
        }

        [TestMethod]
        public void ForRow_CrossingBeforeBaseline_NetIsNull()
        {
            // Phase 2's negative-net guard: a crossing before the participant start is bad
            // data for NET purposes — null, never negative.
            var baseline = Gun.AddMinutes(5);
            var (_, netMs) = DerivedTimes.ForRow(Gun.AddMinutes(3), Gun, baseline, isStartGateRow: false);

            Assert.IsNull(netMs);
        }

        // ─── THE PINNED REPRO (bib 1002): different starts ⇒ different nets ───

        /// <summary>
        /// Punit's byte-identical evidence, inverted into the invariant: finish
        /// 00:32:23.591 after the gun; start toggled :52.487 ↔ :49.679. Net MUST be
        /// finish − start (differs by exactly the toggle delta), and gun − net MUST
        /// equal the start's gun offset.
        /// </summary>
        [TestMethod]
        public void StartToggle_5249_Vs_49679_RecomputesNet_NeverByteIdentical()
        {
            var finishChip = Gun.AddMilliseconds(1_943_591);
            var start52 = Gun.AddMilliseconds(52_487);
            var start49 = Gun.AddMilliseconds(49_679);

            var baseline52 = DerivedTimes.NetBaseline(Gun, true, start52);
            var baseline49 = DerivedTimes.NetBaseline(Gun, true, start49);

            var (gun52, net52) = DerivedTimes.ForRow(finishChip, Gun, baseline52, isStartGateRow: false);
            var (gun49, net49) = DerivedTimes.ForRow(finishChip, Gun, baseline49, isStartGateRow: false);

            Assert.AreEqual(1_943_591L, gun52, "finish gun time is start-independent");
            Assert.AreEqual(1_943_591L, gun49, "finish gun time is start-independent");

            Assert.AreEqual(1_891_104L, net52, "net = finish − :52.487 start");
            Assert.AreEqual(1_893_912L, net49, "net = finish − :49.679 start");
            Assert.AreNotEqual(net52, net49, "two different starts can NEVER share a net time");

            Assert.AreEqual(52_487L, gun52 - net52!.Value, "gun − net = the start's gun offset");
            Assert.AreEqual(49_679L, gun49 - net49!.Value, "gun − net = the start's gun offset");
        }
    }
}
