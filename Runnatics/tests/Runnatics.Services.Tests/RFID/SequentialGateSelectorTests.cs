using Runnatics.Services.RFID;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// #6 DedUpSeconds redefinition (minimum segment time) + #2 offline sequence rule —
    /// SequentialGateSelector. Fixture: gun 00:03:00 UTC, window [gun−1s, gun+1200s].
    /// </summary>
    [TestClass]
    public class SequentialGateSelectorTests
    {
        private static readonly DateTime Gun = new(2026, 6, 29, 0, 3, 0, DateTimeKind.Utc);
        private static readonly DateTime Floor = Gun.AddSeconds(-1);
        private static readonly DateTime Ceiling = Gun.AddSeconds(1200);
        private const int PassGap = 300;

        private static GateCandidate C(long key, DateTime t) => new() { Key = key, Time = t };

        private static GateInput Gate(int id, bool isStart, params GateCandidate[] candidates) =>
            new() { GateId = id, IsStartGate = isStart, Candidates = candidates };

        private static Dictionary<int, long> Select(int? minSegment, params GateInput[] gates) =>
            SequentialGateSelector.SelectChain(gates, Floor, Ceiling, PassGap, minSegment);

        // ─── The client's 5km-loop example: DedUpSeconds = 2100s (35 min) ───

        [TestMethod]
        public void FiveKmLoop_MinSegment2100_ThirtyMinReadDiscarded_ThirtySixMinWins()
        {
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(5)));
            var finish = Gate(2, isStart: false,
                C(20, Gun.AddSeconds(5).AddMinutes(30)),   // 30 min segment < 35 min → discard
                C(21, Gun.AddSeconds(5).AddMinutes(36)));  // 36 min ≥ 35 min → the finish

            var chain = Select(2100, start, finish);

            Assert.AreEqual(10, chain[1]);
            Assert.AreEqual(21, chain[2], "the later reading ≥ DedUpSeconds must be used");
        }

        [TestMethod]
        public void FiveKmLoop_NoReadingMeetsMinSegment_GateUninhabited_DnfPath()
        {
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(5)));
            var finish = Gate(2, isStart: false, C(20, Gun.AddSeconds(5).AddMinutes(30))); // only 30 min

            var chain = Select(2100, start, finish);

            Assert.IsTrue(chain.ContainsKey(1));
            Assert.IsFalse(chain.ContainsKey(2), "no valid reading remains → uninhabited → DNF (#7)");
        }

        // ─── null/0 = feature OFF (the old 30s default is REMOVED) ───

        [TestMethod]
        public void MinSegmentOff_NullOrZero_NoMinimumSegmentCheck()
        {
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(5)));
            // 3 seconds after the start crossing — absurd, but legal with the feature OFF.
            var next = Gate(2, isStart: false, C(20, Gun.AddSeconds(8)));

            Assert.AreEqual(20, SequentialGateSelector.SelectChain(
                new[] { start, next }, Floor, Ceiling, PassGap, null)[2]);
            Assert.AreEqual(20, SequentialGateSelector.SelectChain(
                new[] { start, next }, Floor, Ceiling, PassGap, 0)[2]);
        }

        [TestMethod]
        public void PassCollapseSettings_MinSegment_NullZeroNegative_MeansOff()
        {
            Assert.IsNull(PassCollapseSettings.MinSegmentSeconds(null));
            Assert.IsNull(PassCollapseSettings.MinSegmentSeconds(0));
            Assert.IsNull(PassCollapseSettings.MinSegmentSeconds(-5));
            Assert.AreEqual(2100, PassCollapseSettings.MinSegmentSeconds(2100));
        }

        // ─── #2 sequence rule: strictly after the previous selected crossing ───

        [TestMethod]
        public void Sequence_OutOfOrderReadingDiscarded_NextValidCandidateUsed()
        {
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(30)));
            var mid = Gate(2, isStart: false,
                C(20, Gun.AddSeconds(10)),    // BEFORE the start crossing → discard
                C(21, Gun.AddSeconds(30)),    // EQUAL to the start crossing → not strictly after → discard
                C(22, Gun.AddSeconds(90)));   // valid

            var chain = Select(null, start, mid);

            Assert.AreEqual(22, chain[2]);
        }

        [TestMethod]
        public void Sequence_UninhabitedGate_ChainContinuesFromLastSelected()
        {
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(5)));
            var mid = Gate(2, isStart: false, C(20, Gun.AddSeconds(2)));      // before start → uninhabited
            var finish = Gate(3, isStart: false, C(30, Gun.AddSeconds(900))); // validates against the START crossing

            var chain = Select(null, start, mid, finish);

            Assert.IsFalse(chain.ContainsKey(2));
            Assert.AreEqual(30, chain[3], "the chain anchors on the LAST SELECTED crossing, not the empty gate");
        }

        // ─── Greedy semantics pin (deliberate, per review): NO backtracking ───

        [TestMethod]
        public void Greedy_NoBacktracking_StarvedNextGateIsDnf_NotARevisedEarlierChoice()
        {
            // Gate N's ONLY candidate is at +600s; gate N+1's candidate is EARLIER (+500s).
            // No combination satisfies the order — the selector keeps N and starves N+1 (DNF
            // path), it does NOT drop N to rescue N+1. Pinned so the semantics are explicit.
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(5)));
            var n = Gate(2, isStart: false, C(20, Gun.AddSeconds(600)));
            var nPlus1 = Gate(3, isStart: false, C(30, Gun.AddSeconds(500)));

            var chain = Select(null, start, n, nPlus1);

            Assert.AreEqual(20, chain[2], "gate N keeps its valid candidate");
            Assert.IsFalse(chain.ContainsKey(3), "starved next gate → uninhabited → DNF; no backtracking");
        }

        [TestMethod]
        public void Greedy_EarliestValidAtN_NeverHurtsNPlus1()
        {
            // The formal reason greedy covers "any combination": a LATER choice at N only raises
            // the bound for N+1. Earliest-valid at N (+120s over +600s) lets N+1 (+500s) succeed.
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(5)));
            var n = Gate(2, isStart: false, C(20, Gun.AddSeconds(120)), C(21, Gun.AddSeconds(600)));
            var nPlus1 = Gate(3, isStart: false, C(30, Gun.AddSeconds(500)));

            var chain = Select(null, start, n, nPlus1);

            Assert.AreEqual(20, chain[2], "earliest valid candidate wins at N");
            Assert.AreEqual(30, chain[3], "…which is exactly what keeps N+1 satisfiable");
        }

        // ─── Start-gate handling inside the chain ───

        [TestMethod]
        public void StartGate_UsesStartSelectionInvariant_LastOfFirstInWindowPass()
        {
            var start = Gate(1, isStart: true,
                C(10, Gun.AddSeconds(10)),
                C(11, Gun.AddSeconds(20)));   // LAST of the first in-window pass ← the start
            var finish = Gate(2, isStart: false, C(30, Gun.AddSeconds(1100)));

            var chain = Select(null, start, finish);

            Assert.AreEqual(11, chain[1]);
            Assert.AreEqual(30, chain[2]);
        }

        [TestMethod]
        public void StartGate_OutOfWindowOnly_EarliestKeptAsInvalidPlaceholder_AndAnchorsChain()
        {
            // Retain-not-drop: the placeholder must reach Phase 3/display (classification calls
            // it invalid data); the SEQUENCE still anchors on it — it is a physical crossing.
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(1500)), C(11, Gun.AddSeconds(1510)));
            var finish = Gate(2, isStart: false,
                C(30, Gun.AddSeconds(1400)),   // before their (late) start crossing → discard
                C(31, Gun.AddSeconds(2400)));  // after it → selected

            var chain = Select(null, start, finish);

            Assert.AreEqual(10, chain[1], "out-of-window → EARLIEST candidate kept as the INVALID placeholder");
            Assert.AreEqual(31, chain[2]);
        }

        [TestMethod]
        public void NoStartGateReadings_FirstDataGateUnconstrained()
        {
            // The runner missed the start mat entirely: their first gate with data selects its
            // earliest candidate (nothing to sequence against yet).
            var mid = Gate(2, isStart: false, C(20, Gun.AddSeconds(700)));
            var finish = Gate(3, isStart: false, C(30, Gun.AddSeconds(1500)));

            var chain = Select(null, mid, finish);

            Assert.AreEqual(20, chain[2]);
            Assert.AreEqual(30, chain[3]);
        }

        [TestMethod]
        public void MinSegment_AppliesBetweenEveryConsecutiveSelectedPair()
        {
            // start → mid honors it, and mid → finish honors it from the SELECTED mid crossing.
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(0)));
            var mid = Gate(2, isStart: false,
                C(20, Gun.AddSeconds(50)),    // < 60s from start → discard
                C(21, Gun.AddSeconds(70)));   // ≥ 60s → selected
            var finish = Gate(3, isStart: false,
                C(30, Gun.AddSeconds(120)),   // 50s from mid's 70s → discard
                C(31, Gun.AddSeconds(140)));  // 70s from mid → selected

            var chain = Select(60, start, mid, finish);

            Assert.AreEqual(21, chain[2]);
            Assert.AreEqual(31, chain[3]);
        }
    }
}
