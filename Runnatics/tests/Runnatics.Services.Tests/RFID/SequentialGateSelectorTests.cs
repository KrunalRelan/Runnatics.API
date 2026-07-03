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

        // ─── LOCKED ANCHORS (incremental normalization — manual-time revert / late batches) ───

        private static GateInput Locked(int id, DateTime t, bool isStart = false) =>
            new() { GateId = id, IsStartGate = isStart, LockedCrossingTime = t };

        [TestMethod]
        public void LockedGate_EmitsNoSelection_AndAnchorsTheChain()
        {
            // The revert shape: start already normalized (locked), the reverted mid gate
            // re-selects against it — a candidate before the locked start is discarded.
            var start = Locked(1, Gun.AddSeconds(30), isStart: true);
            var mid = Gate(2, isStart: false,
                C(20, Gun.AddSeconds(10)),    // before the locked start crossing → discard
                C(21, Gun.AddSeconds(90)));   // valid

            var chain = Select(null, start, mid);

            Assert.IsFalse(chain.ContainsKey(1), "locked gates never emit a selection");
            Assert.AreEqual(21, chain[2], "selection anchors on the locked crossing");
        }

        [TestMethod]
        public void LockedLaterGate_UpperBoundsEarlierSelection()
        {
            // A mid-gate revert with the FINISH already normalized: the rebuilt mid crossing must
            // be STRICTLY BEFORE the locked finish — otherwise a fresh reprocess could never have
            // produced the combined state.
            var start = Locked(1, Gun.AddSeconds(10), isStart: true);
            var finish = Locked(3, Gun.AddSeconds(400));

            var midTooLate = Gate(2, isStart: false, C(20, Gun.AddSeconds(500))); // ≥ locked finish
            Assert.IsFalse(Select(null, start, midTooLate, finish).ContainsKey(2),
                "a candidate past the locked next crossing is discarded → gate uninhabited");

            var midOk = Gate(2, isStart: false, C(21, Gun.AddSeconds(200)));
            Assert.AreEqual(21, Select(null, start, midOk, finish)[2]);
        }

        [TestMethod]
        public void LockedNeighbors_MinSegmentAppliesOnBothSides()
        {
            // Locked start at gun+0 and locked finish at gun+1000, min segment 300s: the rebuilt
            // mid crossing must be ≥ start+300 AND ≤ finish−300 — both sides, like a fresh run.
            var start = Locked(1, Gun, isStart: true);
            var finish = Locked(3, Gun.AddSeconds(1000));
            var mid = Gate(2, isStart: false,
                C(20, Gun.AddSeconds(200)),   // only 200s after the locked start → discard
                C(21, Gun.AddSeconds(800)),   // only 200s before the locked finish → discard
                C(22, Gun.AddSeconds(400)));  // candidates are TIME-ORDERED in real input — see below

            // Time-ordered input (200, 400, 800): 400 is the first candidate satisfying both sides.
            var midOrdered = Gate(2, isStart: false,
                C(20, Gun.AddSeconds(200)),
                C(22, Gun.AddSeconds(400)),
                C(21, Gun.AddSeconds(800)));

            Assert.AreEqual(22, Select(300, start, midOrdered, finish)[2]);
            _ = mid; // documents the unordered shape; the selector contract requires time order
        }

        [TestMethod]
        public void StartGate_CandidatePastLockedLaterCrossing_Uninhabited()
        {
            // Reverted START with a locked mid crossing at gun+200: a "start" selected at/after
            // the mid crossing is an order violation a fresh reprocess could never produce.
            var start = Gate(1, isStart: true, C(10, Gun.AddSeconds(300)));   // after the locked mid
            var mid = Locked(2, Gun.AddSeconds(200));

            var chain = Select(null, start, mid);

            Assert.IsFalse(chain.ContainsKey(1), "start past the locked next crossing → uninhabited");
        }

        [TestMethod]
        public void RevertedStart_ReSelectedByStartInvariant_UnderLockedFinish()
        {
            // The screenshot case: manual start reverted; raw start cluster re-enters selection
            // (LAST of first in-window pass) with the finish already normalized (locked).
            var start = Gate(1, isStart: true,
                C(10, Gun.AddSeconds(10)),
                C(11, Gun.AddSeconds(20)));    // LAST of the first in-window pass ← restored start
            var finish = Locked(2, Gun.AddSeconds(1100));

            var chain = Select(null, start, finish);

            Assert.AreEqual(11, chain[1], "the automated start selection returns on revert");
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
