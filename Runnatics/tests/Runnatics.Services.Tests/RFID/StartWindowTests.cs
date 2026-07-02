using Runnatics.Services;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Suite section 1 (window edges), section 2 (settings defaults / guards — the
    /// "5-hour window" class: cut-offs are SECONDS, never minutes) and section 8a
    /// (IST pre-05:30 guns land on the PREVIOUS UTC calendar day; window math must
    /// cross the date boundary transparently).
    /// </summary>
    [TestClass]
    public class StartWindowTests
    {
        // Race-65 shape: gun 05:33:00 IST = 00:03:00 UTC.
        private static readonly DateTime Gun = new(2026, 6, 29, 0, 3, 0, DateTimeKind.Utc);

        // ─── 2a/2b: null / 0 / negative → defaults (the "> 0" guard) ───

        [TestMethod]
        public void EarlySeconds_NullZeroNegative_UseDefault300()
        {
            Assert.AreEqual(300, StartWindow.EarlySeconds(null));
            Assert.AreEqual(300, StartWindow.EarlySeconds(0));
            Assert.AreEqual(300, StartWindow.EarlySeconds(-5));
            Assert.AreEqual(1, StartWindow.EarlySeconds(1));      // race 65's configured 1s
            Assert.AreEqual(30, StartWindow.EarlySeconds(30));
        }

        [TestMethod]
        public void LateSeconds_NullZeroNegative_UseDefault1200()
        {
            Assert.AreEqual(1200, StartWindow.LateSeconds(null));
            Assert.AreEqual(1200, StartWindow.LateSeconds(0));
            Assert.AreEqual(1200, StartWindow.LateSeconds(-1));
            Assert.AreEqual(900, StartWindow.LateSeconds(900));
        }

        // ─── 2c: no RaceSettings row at all → both defaults ───

        [TestMethod]
        public void For_NoSettings_BothDefaults()
        {
            // No RaceSettings row → the caller passes raceSettings?.X == null for both.
            var (floor, ceiling) = StartWindow.For(Gun, null, null);

            Assert.AreEqual(Gun.AddSeconds(-300), floor);
            Assert.AreEqual(Gun.AddSeconds(1200), ceiling);
        }

        // ─── 2d: consumed as SECONDS (300 = 5 minutes, never 300 minutes) ───

        [TestMethod]
        public void For_CutoffsAreSeconds_NotMinutes()
        {
            var (floor, ceiling) = StartWindow.For(Gun, 300, 1200);

            Assert.AreEqual(TimeSpan.FromSeconds(300), Gun - floor,
                "EarlyStartCutOff=300 must move the floor 5 MINUTES back, not 300 minutes");
            Assert.AreEqual(TimeSpan.FromSeconds(1200), ceiling - Gun,
                "LateStartCutOff=1200 must move the ceiling 20 MINUTES forward");

            // Race-65 config: 1s cutoff → floor exactly gun − 1s.
            var (floor65, _) = StartWindow.For(Gun, 1, null);
            Assert.AreEqual(Gun.AddSeconds(-1), floor65);
        }

        // ─── 1a-1c: the window EDGES are what the consumers compare against ───

        [TestMethod]
        public void For_WindowEdges_AreExactInstants()
        {
            var (floor, ceiling) = StartWindow.For(Gun, 1, 1200);

            Assert.AreEqual(new DateTime(2026, 6, 29, 0, 2, 59, DateTimeKind.Utc), floor);
            Assert.AreEqual(new DateTime(2026, 6, 29, 0, 23, 0, DateTimeKind.Utc), ceiling);
            // Inclusivity of the edges (>= floor, <= ceiling) is the CONSUMER's contract —
            // asserted row-by-row in ResultClassifierTests (1a/1b/1c).
        }

        // ─── Nullable-gun overload ───

        [TestMethod]
        public void For_NullGun_ReturnsNullWindow()
        {
            var (floor, ceiling) = StartWindow.For((DateTime?)null, 300, 1200);

            Assert.IsNull(floor);
            Assert.IsNull(ceiling);
        }

        // ─── 8a: IST pre-05:30 gun → previous UTC day; window crosses midnight ───

        [TestMethod]
        public void For_PreDawnIstGun_WindowCrossesUtcMidnightCorrectly()
        {
            // 05:00 IST on 2026-06-29 = 23:30 UTC on 2026-06-28 (the midnight-rollback fact).
            var gunUtc = new DateTime(2026, 6, 28, 23, 30, 0, DateTimeKind.Utc);

            var (floor, ceiling) = StartWindow.For(gunUtc, 300, 1800);

            Assert.AreEqual(new DateTime(2026, 6, 28, 23, 25, 0, DateTimeKind.Utc), floor);
            Assert.AreEqual(new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc), ceiling,
                "Ceiling must roll over to the next UTC day — pure UTC arithmetic, no date clamping");
            Assert.AreEqual(28, floor.Day);
            Assert.AreEqual(29, ceiling.Day);
        }

        [TestMethod]
        public void For_GunJustAfterUtcMidnight_FloorLandsOnPreviousUtcDay()
        {
            // Race-65 gun 00:03:00 UTC with the default 300s cutoff → floor 23:58 PREVIOUS day.
            var (floor, _) = StartWindow.For(Gun, null, null);

            Assert.AreEqual(new DateTime(2026, 6, 28, 23, 58, 0, DateTimeKind.Utc), floor);
        }
    }
}
