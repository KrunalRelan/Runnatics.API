using Runnatics.Services;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Suite section 1 — the START WINDOW / DNS truth table, row by row (a–l), against
    /// ResultClassifier (the extracted Phase 3 decision). Fixture mirrors race 65:
    /// gun 00:03:00 UTC, EarlyStartCutOff 1s → floor 00:02:59, LateStartCutOff 1200s →
    /// ceiling 00:23:00.
    ///
    /// Input contract (documented on the classifier): earliestStartRead is the participant's
    /// earliest NORMALIZED start-gate row. Phase 2 keeps the earliest VALID in-window read when
    /// one exists (else the earliest available as an INVALID placeholder) — so row l
    /// ("early + in-window → in-window wins") holds because the early read never reaches the
    /// classifier when an in-window read exists.
    /// </summary>
    [TestClass]
    public class ResultClassifierTests
    {
        private static readonly DateTime Gun = new(2026, 6, 29, 0, 3, 0, DateTimeKind.Utc);
        private static readonly DateTime Floor = Gun.AddSeconds(-1);        // 00:02:59
        private static readonly DateTime Ceiling = Gun.AddSeconds(1200);    // 00:23:00

        private static ParticipantOutcome Classify(DateTime? start, bool covered, bool negativeFinish = false) =>
            ResultClassifier.Classify(start, Floor, Ceiling, covered, negativeFinish);

        // ─── 1a/1b: boundaries are INCLUSIVE ───

        [TestMethod]
        public void RowA_ReadExactlyAtFloor_IsValidStart()
        {
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(Floor, covered: true));
            Assert.AreEqual(ParticipantOutcome.DNF, Classify(Floor, covered: false),
                "At-floor read is a VALID start — non-finisher is DNF (started, didn't finish), not DNS");
        }

        [TestMethod]
        public void RowB_ReadExactlyAtCeiling_IsValidStart()
        {
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(Ceiling, covered: true));
            Assert.AreEqual(ParticipantOutcome.DNF, Classify(Ceiling, covered: false));
        }

        // ─── 1c: one second outside either edge is OUT ───

        [TestMethod]
        public void RowC_OneSecondBeforeFloor_IsInvalid_EarlyTaint()
        {
            // Early is a TAINT: DNS even when every mandatory gate was covered.
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(Floor.AddSeconds(-1), covered: true));
        }

        [TestMethod]
        public void RowC_OneSecondAfterCeiling_IsInvalidAsStart()
        {
            // Late is NOT a taint — the consequence depends on coverage (rows g/h).
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(Ceiling.AddSeconds(1), covered: true));
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(Ceiling.AddSeconds(1), covered: false));
        }

        // ─── 1d: in-window + finisher → Finished ───

        [TestMethod]
        public void RowD_InWindowStart_Finisher_Finished()
        {
            // 00:03:34 — bib 5176's real start.
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(Gun.AddSeconds(34), covered: true));
        }

        [TestMethod]
        public void InWindowStart_NonFinisher_DNF()
        {
            Assert.AreEqual(ParticipantOutcome.DNF, Classify(Gun.AddSeconds(34), covered: false));
        }

        // ─── 1e/1f: pre-floor only → DNS regardless of coverage ───

        [TestMethod]
        public void RowE_OnlyPreFloorRead_Finisher_DNS()
        {
            // The early taint beats finish data — 05:32:37 IST class.
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(Gun.AddSeconds(-23), covered: true));
        }

        [TestMethod]
        public void RowF_OnlyPreFloorRead_NonFinisher_DNS()
        {
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(Gun.AddSeconds(-23), covered: false));
        }

        // ─── 1g/1h: late-only read ───

        [TestMethod]
        public void RowG_OnlyLateRead_Finisher_KeptFinished()
        {
            // Finisher-safe: a late start read is not a "did not start"; NetTime nets from the gun.
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(Ceiling.AddMinutes(10), covered: true));
        }

        [TestMethod]
        public void RowH_OnlyLateRead_NonFinisher_DNS()
        {
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(Ceiling.AddMinutes(10), covered: false));
        }

        // ─── 1i/1j: no start read at all ───

        [TestMethod]
        public void RowI_NoStartRead_Finisher_KeptFinished()
        {
            // Reader-miss at the start mat must not erase a demonstrable run (Row 5 / finisher-safe).
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(null, covered: true));
        }

        [TestMethod]
        public void RowJ_NoStartRead_NonFinisher_DNS()
        {
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(null, covered: false));
        }

        // ─── 1k/1l: both-sides reads ───

        [TestMethod]
        public void RowK_EarlyAndLateReads_NoneInWindow_EarlyTaintWins_DNS()
        {
            // Phase 3 receives the EARLIEST normalized start read → the early one → DNS,
            // regardless of coverage (early taint beats the late finisher-safe rule).
            var earliest = Floor.AddMinutes(-2); // min(early, late) = early
            Assert.AreEqual(ParticipantOutcome.DNS, Classify(earliest, covered: true));
        }

        [TestMethod]
        public void RowL_EarlyAndInWindowReads_InWindowWins_Finished()
        {
            // Phase 2 keeps the earliest VALID in-window read as THE normalized start row when
            // one exists — the pre-floor read never reaches the classifier. What arrives here
            // is the in-window read.
            var normalizedStart = Gun.AddSeconds(34);
            Assert.AreEqual(ParticipantOutcome.Finished, Classify(normalizedStart, covered: true));
        }

        // ─── 4a: negative finish time → DNF, highest precedence ───

        [TestMethod]
        public void NegativeFinishTime_AlwaysDNF_EvenWithValidStart()
        {
            Assert.AreEqual(ParticipantOutcome.DNF,
                Classify(Gun.AddSeconds(34), covered: true, negativeFinish: true));
        }

        [TestMethod]
        public void NegativeFinishTime_BeatsEarlyTaint_DNF()
        {
            // Precedence documented in the classifier: the impossible-time flag is checked FIRST.
            Assert.AreEqual(ParticipantOutcome.DNF,
                Classify(Floor.AddMinutes(-5), covered: true, negativeFinish: true));
        }

        // ─── No-window fallback (no gun — shouldn't happen post-validation) ───

        [TestMethod]
        public void NoWindow_AnyReadIsValidStart()
        {
            Assert.AreEqual(ParticipantOutcome.Finished,
                ResultClassifier.Classify(Gun.AddHours(-5), null, null, allMandatoryCovered: true, hasNegativeFinishTime: false));
            Assert.AreEqual(ParticipantOutcome.DNF,
                ResultClassifier.Classify(Gun.AddHours(-5), null, null, allMandatoryCovered: false, hasNegativeFinishTime: false));
        }

        [TestMethod]
        public void NoWindow_NoRead_CoverageDecides()
        {
            Assert.AreEqual(ParticipantOutcome.Finished,
                ResultClassifier.Classify(null, null, null, allMandatoryCovered: true, hasNegativeFinishTime: false));
            Assert.AreEqual(ParticipantOutcome.DNS,
                ResultClassifier.Classify(null, null, null, allMandatoryCovered: false, hasNegativeFinishTime: false));
        }

        // ─── 8a: the table works unchanged across the UTC date boundary ───

        [TestMethod]
        public void PreDawnIstGun_EarlyReadOnPreviousUtcDay_StillDNS()
        {
            // Gun 00:03 UTC (05:33 IST); an early read at 23:59 UTC the PREVIOUS day is pre-floor.
            var (floor, ceiling) = StartWindow.For(Gun, 300, 1200);
            var earlyPrevDay = new DateTime(2026, 6, 28, 23, 55, 0, DateTimeKind.Utc); // < floor 23:58

            Assert.AreEqual(ParticipantOutcome.DNS,
                ResultClassifier.Classify(earlyPrevDay, floor, ceiling, allMandatoryCovered: true, hasNegativeFinishTime: false));

            // And an in-window read on the previous UTC day (floor 23:58 ≤ read < 00:00) is VALID.
            var inWindowPrevDay = new DateTime(2026, 6, 28, 23, 59, 0, DateTimeKind.Utc);
            Assert.AreEqual(ParticipantOutcome.Finished,
                ResultClassifier.Classify(inWindowPrevDay, floor, ceiling, allMandatoryCovered: true, hasNegativeFinishTime: false));
        }
    }
}
