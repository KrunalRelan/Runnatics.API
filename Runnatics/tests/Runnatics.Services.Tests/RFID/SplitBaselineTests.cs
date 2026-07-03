using Runnatics.Services;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// SplitBaseline — the NET split/cumulative baseline (client rule: splits and cumulative are
    /// measured from the RUNNER'S OWN valid start crossing, never the gun; the Start row is
    /// 00:00/00:00 always; the gun-to-start offset is a separate value).
    ///
    /// INVARIANT under test: cumulative at the Finish == Results.NetTime — for a valid-start
    /// runner AND for a late-only finisher (whose NetTime is gun-clamped, baseline 0).
    /// </summary>
    [TestClass]
    public class SplitBaselineTests
    {
        // ─── Validity gate + defaulting (LateStartCutOff must go through StartWindow) ───

        [TestMethod]
        public void BaselineMs_ValidStartRow_ReturnsItsOffset()
        {
            // Bib 5176: start crossing 34s after the gun, default ceiling 1200s → valid.
            Assert.AreEqual(34_000L, SplitBaseline.BaselineMs(34_000L, null));
        }

        [TestMethod]
        public void BaselineMs_CutoffDefaultsViaStartWindow_NullAndZeroMean1200s()
        {
            // The trap this guards: a RAW column read of a null LateStartCutOff would make the
            // ceiling 0 and every start row "invalid" — baseline silently gun for everyone.
            Assert.AreEqual(1_200_000L, SplitBaseline.BaselineMs(1_200_000L, null),
                "null cutoff → 1200s ceiling (inclusive), not 0");
            Assert.AreEqual(1_200_000L, SplitBaseline.BaselineMs(1_200_000L, 0),
                "0 cutoff → default too (the > 0 guard)");
            Assert.AreEqual(0L, SplitBaseline.BaselineMs(1_200_001L, null),
                "1ms past the default ceiling → late placeholder → gun baseline");
        }

        [TestMethod]
        public void BaselineMs_CustomCutoff_Respected()
        {
            Assert.AreEqual(1_500_000L, SplitBaseline.BaselineMs(1_500_000L, 1800),
                "within a custom 1800s ceiling → valid");
            Assert.AreEqual(0L, SplitBaseline.BaselineMs(1_500_000L, 1200),
                "beyond a custom 1200s ceiling → gun baseline");
        }

        [TestMethod]
        public void BaselineMs_LateOnlyPlaceholder_GunBaseline()
        {
            // Late-only finisher: their start row is the invalid placeholder; NetTime nets from
            // the gun, so the split baseline must be the gun too (invariant holds).
            Assert.AreEqual(0L, SplitBaseline.BaselineMs(1_620_000L, null));
        }

        [TestMethod]
        public void BaselineMs_NoStartRow_GunBaseline()
        {
            Assert.AreEqual(0L, SplitBaseline.BaselineMs(null, null));
        }

        [TestMethod]
        public void BaselineMs_NegativeRow_ClampedToGun()
        {
            // Unreachable in storage (both writers skip negative-ms rows) — the clamp mirrors
            // the Phase 2 gun clamp (BUG-27) defensively.
            Assert.AreEqual(0L, SplitBaseline.BaselineMs(-5_000L, null));
        }

        // ─── Cumulative math + the Finish == NetTime invariant ───

        [TestMethod]
        public void Cumulative_Bib5176_StartZero_FinishEqualsNetTime()
        {
            // Race 65 after the collapse fix: start row 34s, 2.5K at ~9:00, finish at 18:53 gun.
            var baseline = SplitBaseline.BaselineMs(34_000L, null);

            Assert.AreEqual(0L, SplitBaseline.CumulativeMs(34_000L, baseline), "Start row → 00:00");
            Assert.AreEqual(506_000L, SplitBaseline.CumulativeMs(540_000L, baseline), "2.5K net cumulative");

            // Finish: gun 1133s → net 1099s = 18:19 — MUST equal NetTime
            // (NetTime = finishChip − max(gun, startCrossing) = same subtraction).
            var finishCumulative = SplitBaseline.CumulativeMs(1_133_000L, baseline);
            var netTime = 1_133_000L - 34_000L;
            Assert.AreEqual(netTime, finishCumulative, "INVARIANT: cumulative at Finish == NetTime");
            Assert.AreEqual(1_099_000L, finishCumulative, "18:19 — the plausible 5K, not 18:53");
        }

        [TestMethod]
        public void Cumulative_LateOnlyStart_GunBaselineForDisplay()
        {
            // Start row 25 min after the gun (past the ceiling) → baseline gun.
            // MEANING CHANGE (#7, 2026-07-03): this runner used to be a KEPT finisher
            // (finisher-safe, netting from the gun) — they are now DNF (invalid start data).
            // The gun-fallback baseline REMAINS the display rule for their split table.
            var baseline = SplitBaseline.BaselineMs(1_500_000L, null);
            Assert.AreEqual(0L, baseline);

            var finishGunMs = 3_600_000L;
            Assert.AreEqual(finishGunMs, SplitBaseline.CumulativeMs(finishGunMs, baseline),
                "late-only start: displayed cumulative measures from the gun");
        }

        [TestMethod]
        public void Cumulative_NeverNegative_AndNullSafe()
        {
            Assert.AreEqual(0L, SplitBaseline.CumulativeMs(10_000L, 34_000L),
                "a row before the baseline clamps to 0, never negative output");
            Assert.AreEqual(0L, SplitBaseline.CumulativeMs(null, 34_000L));
        }

        [TestMethod]
        public void Cumulative_GunToStartOffset_IsSeparateFromCumulative()
        {
            // The corral offset (Gun − Net) is representable but NEVER a cumulative:
            // for bib 5176 it is 34s — and no checkpoint's cumulative includes it.
            var baseline = SplitBaseline.BaselineMs(34_000L, null);
            var gunAtFinish = 1_133_000L;
            var netAtFinish = SplitBaseline.CumulativeMs(gunAtFinish, baseline);

            Assert.AreEqual(34_000L, gunAtFinish - netAtFinish, "Gun = Net + offset stays reconcilable");
        }
    }
}
