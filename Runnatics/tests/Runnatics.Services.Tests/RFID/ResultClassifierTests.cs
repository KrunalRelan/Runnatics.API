using Runnatics.Models.Data.Entities;
using Runnatics.Services;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Suite section — #7 STATUS DEFINITIONS (client-confirmed 2026-07-03, REWRITES the old
    /// truth table):
    ///   OK/Finished — valid data at ALL mandatory gates · DNF — ANY mandatory gate's data
    ///   missing/invalid · DNS — NO valid data at ANY mandatory gate. Invalid reads are not data.
    ///
    /// MEANING CHANGES vs the pre-2026-07-03 table (for the spec doc):
    ///   - late-only start + finish data: Finished → DNF (late-only-finisher keep REMOVED)
    ///   - no start read + finish data (Row-5): Finished → DNF (finisher-safe REMOVED)
    ///   - pre-floor start + finish data: DNS → DNF (early taint collapsed into "invalid data")
    ///   - early+late, none in-window, + finish data: DNS → DNF
    ///   Unchanged: valid start + coverage → Finished; valid start + gaps → DNF;
    ///   invalid-only / no data → DNS.
    ///
    /// Start-gate VALIDITY is StartWindow.Contains (boundaries inclusive) — tested here as the
    /// input contract; the 3-way rule itself is ResultClassifier.Classify.
    /// </summary>
    [TestClass]
    public class ResultClassifierTests
    {
        private static readonly DateTime Gun = new(2026, 6, 29, 0, 3, 0, DateTimeKind.Utc);
        private static readonly DateTime Floor = Gun.AddSeconds(-1);        // 00:02:59
        private static readonly DateTime Ceiling = Gun.AddSeconds(1200);    // 00:23:00

        /// <summary>
        /// Mirrors the caller contract at every classification site: a 3-gate race
        /// (start / mid / finish, all mandatory) where each flag says "this gate has VALID data".
        /// </summary>
        private static ParticipantOutcome Scenario(bool startValid, bool midValid, bool finishValid) =>
            ResultClassifier.Classify(
                (startValid ? 1 : 0) + (midValid ? 1 : 0) + (finishValid ? 1 : 0),
                totalMandatoryGates: 3);

        // ─── The 3-way rule ───

        [TestMethod]
        public void AllGatesValid_Finished()
        {
            Assert.AreEqual(ParticipantOutcome.Finished, Scenario(true, true, true));
        }

        [TestMethod]
        public void SomeGatesValid_DNF()
        {
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(true, false, true), "mid gate missing");
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(false, true, true), "start invalid/missing");
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(true, true, false), "finish missing/invalid");
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(false, false, true), "only the finish");
        }

        [TestMethod]
        public void NoGateValid_DNS()
        {
            Assert.AreEqual(ParticipantOutcome.DNS, Scenario(false, false, false));
        }

        [TestMethod]
        public void DegenerateZeroGates_DNS()
        {
            Assert.AreEqual(ParticipantOutcome.DNS, ResultClassifier.Classify(0, 0));
        }

        // ─── The killed rows (meaning changes, asserted deliberately) ───

        [TestMethod]
        public void FinisherSafe_IsDead_NoStartData_FullFinishData_DNF()
        {
            // Pre-#7: "Row-5 keep" — a finisher with a missing start read stayed Finished.
            // Now the start gate is mandatory and its data is missing → DNF.
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(startValid: false, midValid: true, finishValid: true));
        }

        [TestMethod]
        public void LateOnlyFinisher_IsDead_DNF()
        {
            // Pre-#7: a start read past the ceiling + finish data stayed Finished (net from gun).
            // A late read is INVALID start data → DNF.
            var lateRead = Ceiling.AddMinutes(10);
            var startValid = StartWindow.Contains(lateRead, Floor, Ceiling);
            Assert.IsFalse(startValid);
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(startValid, true, true));
        }

        [TestMethod]
        public void EarlyTaint_IsDead_PreFloorStartWithFinishData_DNF_NotDNS()
        {
            // Pre-#7: pre-floor start + finish data → DNS for the whole run ("early taint").
            // Now the early read is simply invalid data at the start gate → DNF.
            var earlyRead = Floor.AddSeconds(-1);
            var startValid = StartWindow.Contains(earlyRead, Floor, Ceiling);
            Assert.IsFalse(startValid);
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(startValid, true, true));
        }

        [TestMethod]
        public void InvalidReadsOnly_DNS()
        {
            // "Invalid reads do NOT count as data": a runner whose ONLY reads are pre-floor /
            // out-of-window / discarded has no valid data anywhere → DNS.
            Assert.AreEqual(ParticipantOutcome.DNS, Scenario(false, false, false));
        }

        [TestMethod]
        public void NegativeFinishTime_InvalidFinishData_DNF_OrDNSWhenOnlyData()
        {
            // An impossible (negative) finish time = INVALID data at the finish gate.
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(startValid: true, midValid: true, finishValid: false));
            Assert.AreEqual(ParticipantOutcome.DNS, Scenario(startValid: false, midValid: false, finishValid: false),
                "negative finish as their only read → invalid-only → DNS");
        }

        // ─── Unchanged rows ───

        [TestMethod]
        public void ValidStart_AllCovered_Finished_Unchanged()
        {
            var startValid = StartWindow.Contains(Gun.AddSeconds(34), Floor, Ceiling);
            Assert.IsTrue(startValid);
            Assert.AreEqual(ParticipantOutcome.Finished, Scenario(startValid, true, true));
        }

        [TestMethod]
        public void ValidStart_MissingMidGate_DNF_Unchanged()
        {
            Assert.AreEqual(ParticipantOutcome.DNF, Scenario(true, false, true));
        }

        [TestMethod]
        public void EarlyAndInWindowReads_InWindowWins_Finished_Unchanged()
        {
            // Phase 2 stores the SELECTED valid start (LAST of first in-window pass) — the early
            // read never reaches classification when an in-window read exists.
            var normalizedStart = Gun.AddSeconds(34);
            Assert.IsTrue(StartWindow.Contains(normalizedStart, Floor, Ceiling));
            Assert.AreEqual(ParticipantOutcome.Finished, Scenario(true, true, true));
        }

        // ─── Start-gate validity: StartWindow.Contains (boundaries INCLUSIVE) ───

        [TestMethod]
        public void Contains_BoundariesInclusive()
        {
            Assert.IsTrue(StartWindow.Contains(Floor, Floor, Ceiling), "exactly AT the floor is valid");
            Assert.IsTrue(StartWindow.Contains(Ceiling, Floor, Ceiling), "exactly AT the ceiling is valid");
            Assert.IsFalse(StartWindow.Contains(Floor.AddSeconds(-1), Floor, Ceiling));
            Assert.IsFalse(StartWindow.Contains(Ceiling.AddSeconds(1), Floor, Ceiling));
        }

        [TestMethod]
        public void Contains_NullWindow_AnyReadValid()
        {
            // No gun (shouldn't happen post-validation) → historical fallback: any read valid.
            Assert.IsTrue(StartWindow.Contains(Gun.AddHours(-5), null, null));
        }

        [TestMethod]
        public void Contains_CrossesUtcMidnight()
        {
            // Gun 00:03 UTC (05:33 IST): floor 23:58 PREVIOUS UTC day with the default cutoff.
            var (floor, ceiling) = StartWindow.For(Gun, null, null);
            Assert.IsTrue(StartWindow.Contains(new DateTime(2026, 6, 28, 23, 59, 0, DateTimeKind.Utc), floor, ceiling));
            Assert.IsFalse(StartWindow.Contains(new DateTime(2026, 6, 28, 23, 55, 0, DateTimeKind.Utc), floor, ceiling));
        }

        // ─── MandatoryDistances: {start, implicitly} ∪ {IsMandatory} ∪ {finish fallback} ───

        private static Checkpoint Cp(int id, decimal distance, bool mandatory = false, int? parentDeviceId = null) =>
            new()
            {
                Id = id,
                EventId = 38,
                RaceId = 65,
                DeviceId = 2,
                ParentDeviceId = parentDeviceId,
                DistanceFromStart = distance,
                IsMandatory = mandatory,
                Name = $"CP{distance}"
            };

        [TestMethod]
        public void MandatoryDistances_StartIsImplicitlyMandatory_WithFlaggedGates()
        {
            // Start (0) unflagged, 2.5 flagged — the start joins the set anyway.
            var gates = ResultClassifier.MandatoryDistances(new[]
            {
                Cp(1, 0m), Cp(2, 2.5m, mandatory: true), Cp(3, 5m)
            });

            CollectionAssert.AreEqual(new[] { 0m, 2.5m }, gates.ToArray());
        }

        [TestMethod]
        public void MandatoryDistances_NoFlags_StartPlusFinishFallback()
        {
            var gates = ResultClassifier.MandatoryDistances(new[]
            {
                Cp(1, 0m), Cp(2, 2.5m), Cp(3, 5m)
            });

            CollectionAssert.AreEqual(new[] { 0m, 5m }, gates.ToArray());
        }

        [TestMethod]
        public void MandatoryDistances_SharedMat_DistanceKeyed_OneGatePerDistance()
        {
            // Race-65 shape: primary + child at 0.0 and 5.0 (shared start/finish mat) — the start
            // gate is the DISTANCE-0 gate, once, regardless of how many devices sit on it.
            var gates = ResultClassifier.MandatoryDistances(new[]
            {
                Cp(396, 0m), Cp(429, 0m, parentDeviceId: 2),
                Cp(398, 2.5m),
                Cp(430, 5m), Cp(431, 5m, parentDeviceId: 2)
            });

            CollectionAssert.AreEqual(new[] { 0m, 5m }, gates.ToArray());
        }

        [TestMethod]
        public void MandatoryDistances_StartAlreadyFlagged_NoDuplicate()
        {
            var gates = ResultClassifier.MandatoryDistances(new[]
            {
                Cp(1, 0m, mandatory: true), Cp(2, 5m, mandatory: true)
            });

            CollectionAssert.AreEqual(new[] { 0m, 5m }, gates.ToArray());
        }

        [TestMethod]
        public void MandatoryDistances_EmptyConfig_EmptySet()
        {
            Assert.AreEqual(0, ResultClassifier.MandatoryDistances(Array.Empty<Checkpoint>()).Count);
        }
    }
}
