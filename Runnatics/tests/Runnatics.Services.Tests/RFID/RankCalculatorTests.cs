using Runnatics.Models.Data.Entities;
using Runnatics.Services;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// Suite section 6 — RankCalculator (the single stored-rank source; BUG-24 per-view basis).
    /// AssignRanks/ResolveBasis are pure; 6d (only Finished ranked) and 6g (pipeline and
    /// interactive paths produce identical stored ranks) are structural: both call sites load
    /// Status == "Finished" only and funnel through RankCalculator.ApplyStoredRanksAsync
    /// (ResultsService.cs:1398, RFIDImportService.cs:3106/:4010) — asserted here via
    /// determinism (same input, any order → identical ranks).
    /// </summary>
    [TestClass]
    public class RankCalculatorTests
    {
        private static Results R(int pid, long? net, long? gun, string? gender = "M", string? category = "18-29") =>
            new()
            {
                ParticipantId = pid,
                NetTime = net,
                GunTime = gun,
                Status = "Finished",
                Participant = new Participant { Gender = gender, AgeCategory = category }
            };

        // ─── 6a: RankOnNet true → NetTime; false → GunTime ───

        [TestMethod]
        public void OverallBasis_Net_RanksByNetTime()
        {
            // Net order A,B,C — gun order C,B,A (staggered start makes them diverge).
            var a = R(1, net: 100, gun: 300);
            var b = R(2, net: 200, gun: 200);
            var c = R(3, net: 300, gun: 100);

            RankCalculator.AssignRanks(new[] { a, b, c }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, a.OverallRank);
            Assert.AreEqual(2, b.OverallRank);
            Assert.AreEqual(3, c.OverallRank);
        }

        [TestMethod]
        public void OverallBasis_Gun_RanksByGunTime()
        {
            var a = R(1, net: 100, gun: 300);
            var b = R(2, net: 200, gun: 200);
            var c = R(3, net: 300, gun: 100);

            RankCalculator.AssignRanks(new[] { a, b, c }, overallBasis: false, categoryBasis: false);

            Assert.AreEqual(3, a.OverallRank);
            Assert.AreEqual(2, b.OverallRank);
            Assert.AreEqual(1, c.OverallRank);
        }

        // ─── 6b: per-view — overall and category on DIFFERENT bases (BUG-24) ───

        [TestMethod]
        public void PerViewBases_OverallNet_CategoryGun_RanksDiverge()
        {
            var a = R(1, net: 100, gun: 300);
            var b = R(2, net: 300, gun: 100);

            RankCalculator.AssignRanks(new[] { a, b }, overallBasis: true, categoryBasis: false);

            Assert.AreEqual(1, a.OverallRank, "overall by NET: a first");
            Assert.AreEqual(2, b.OverallRank);
            Assert.AreEqual(2, a.CategoryRank, "category by GUN: a second");
            Assert.AreEqual(1, b.CategoryRank);
        }

        // ─── 6c: ties — SHARED competition numbering (1,2,2,4; 2026-08 client decision).
        //     Equal primary times share a rank; the next distinct time resumes at its ordinal. ───

        [TestMethod]
        public void Tie_PrimaryEqual_SharesRank()
        {
            var a = R(1, net: 100, gun: 150);
            var b = R(2, net: 100, gun: 140);   // same net → same chip rank, gun times differ

            RankCalculator.AssignRanks(new[] { a, b }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, a.OverallRank, "equal primary time shares rank 1");
            Assert.AreEqual(1, b.OverallRank);
            Assert.AreEqual(1, b.GunOverallRank, "gun basis is NOT tied — faster gun ranks 1");
            Assert.AreEqual(2, a.GunOverallRank);
        }

        [TestMethod]
        public void Tie_FullyEqual_SharesRank_CompetitionNumberingSkips()
        {
            // 1,2,2,4: tied pair shares rank 2; next runner resumes at ordinal 4.
            var first = R(1, net: 100, gun: 100);
            var tied1 = R(7, net: 200, gun: 200);
            var tied2 = R(3, net: 200, gun: 200);
            var next  = R(5, net: 300, gun: 300);

            RankCalculator.AssignRanks(new[] { first, tied1, tied2, next }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, first.OverallRank);
            Assert.AreEqual(2, tied1.OverallRank, "fully equal times share the rank");
            Assert.AreEqual(2, tied2.OverallRank);
            Assert.AreEqual(4, next.OverallRank, "competition numbering skips the absorbed ordinal");
        }

        [TestMethod]
        public void Tie_NullTimes_NeverShareARank()
        {
            var a = R(1, net: null, gun: null);
            var b = R(2, net: null, gun: null);
            var c = R(3, net: 100, gun: 100);

            RankCalculator.AssignRanks(new[] { a, b, c }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, c.OverallRank);
            CollectionAssert.AreEquivalent(new int?[] { 2, 3 },
                new[] { a.OverallRank, b.OverallRank },
                "null times sort last with distinct ordinals — absent data is not a tie");
        }

        [TestMethod]
        public void DualSets_BothBasesPopulated_LegacyCopiesConfiguredBasis()
        {
            // Net order a,b — gun order b,a. Legacy follows the configured basis (net here).
            var a = R(1, net: 100, gun: 300);
            var b = R(2, net: 200, gun: 100);

            RankCalculator.AssignRanks(new[] { a, b }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, a.NetOverallRank);
            Assert.AreEqual(2, b.NetOverallRank);
            Assert.AreEqual(2, a.GunOverallRank);
            Assert.AreEqual(1, b.GunOverallRank);
            Assert.AreEqual(a.NetOverallRank, a.OverallRank, "legacy = configured (net) basis");
            Assert.AreEqual(a.NetGenderRank, a.GenderRank);
            Assert.AreEqual(a.NetCategoryRank, a.CategoryRank);
        }

        [TestMethod]
        public void Ranks_StableAcrossRunsAndInputOrder()
        {
            // 6g's testable core: identical data → identical stored ranks, no matter which
            // path ran or how the rows were ordered coming out of the database.
            var runners = new[]
            {
                R(1, 100, 150), R(2, 100, 150), R(3, 90, 200, "F"),
                R(4, null, 120), R(5, 100, 140, "F", "30-39")
            };

            RankCalculator.AssignRanks(runners, overallBasis: true, categoryBasis: false);
            var firstRun = runners.Select(r => (r.ParticipantId, r.OverallRank, r.GenderRank, r.CategoryRank)).ToList();

            var reversed = Enumerable.Reverse(runners).ToArray();
            RankCalculator.AssignRanks(reversed, overallBasis: true, categoryBasis: false);
            var secondRun = runners.Select(r => (r.ParticipantId, r.OverallRank, r.GenderRank, r.CategoryRank)).ToList();

            CollectionAssert.AreEqual(firstRun, secondRun, "re-ranking must be a fixed point regardless of input order");
        }

        // ─── 6d: null times rank last (DNF/DNS never reach here — caller passes Finished only) ───

        [TestMethod]
        public void NullTimes_SortLast_NotFirst()
        {
            var a = R(1, net: null, gun: null);
            var b = R(2, net: 500, gun: 500);

            RankCalculator.AssignRanks(new[] { a, b }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, b.OverallRank);
            Assert.AreEqual(2, a.OverallRank, "null time = long.MaxValue → last, never rank 1");
        }

        // ─── 6e: gender — canonical M/F only; strays get NULL GenderRank, no phantom groups ───

        [TestMethod]
        public void Gender_NonCanonicalValues_GetNullGenderRank()
        {
            var m = R(1, 100, 100, "M");
            var f = R(2, 110, 110, "F");
            var male = R(3, 120, 120, "Male");   // enum-vs-DB-string class — must NOT form a phantom group
            var blank = R(4, 130, 130, "");
            var none = R(5, 140, 140, null);

            RankCalculator.AssignRanks(new[] { m, f, male, blank, none }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, m.GenderRank);
            Assert.AreEqual(1, f.GenderRank);
            Assert.IsNull(male.GenderRank, "\"Male\" ≠ \"M\" — no phantom rank-of-1 group");
            Assert.IsNull(blank.GenderRank);
            Assert.IsNull(none.GenderRank);
            // Still ranked overall.
            Assert.IsNotNull(male.OverallRank);
            Assert.IsNotNull(blank.OverallRank);
        }

        // ─── 6f: category — "Unknown"/blank skipped (BUG-12) ───

        [TestMethod]
        public void Category_UnknownOrBlank_GetNullCategoryRank()
        {
            var ranked = R(1, 100, 100, "M", "18-29");
            var unknown = R(2, 110, 110, "M", "Unknown");
            var unknownLower = R(3, 120, 120, "M", "unknown");
            var blank = R(4, 130, 130, "M", "");
            var none = R(5, 140, 140, "M", null);

            RankCalculator.AssignRanks(new[] { ranked, unknown, unknownLower, blank, none },
                overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, ranked.CategoryRank);
            Assert.IsNull(unknown.CategoryRank);
            Assert.IsNull(unknownLower.CategoryRank, "case-insensitive Unknown");
            Assert.IsNull(blank.CategoryRank);
            Assert.IsNull(none.CategoryRank);
        }

        // ─── Category is scoped to (Gender, AgeCategory): per-gender ranking within a bracket ───

        [TestMethod]
        public void Category_GenderScoped_MenAndWomenRankSeparately()
        {
            // Same bracket, interleaved times: each gender ranks 1..N independently.
            var m1 = R(1, 100, 100, "M", "18-29");
            var f1 = R(2, 110, 110, "F", "18-29");
            var m2 = R(3, 120, 120, "M", "18-29");
            var f2 = R(4, 130, 130, "F", "18-29");

            RankCalculator.AssignRanks(new[] { m1, f1, m2, f2 }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, m1.CategoryRank);
            Assert.AreEqual(1, f1.CategoryRank, "fastest female is category 1 despite slower time than m1");
            Assert.AreEqual(2, m2.CategoryRank);
            Assert.AreEqual(2, f2.CategoryRank);
        }

        [TestMethod]
        public void Category_StrayGender_GetsNullCategoryRank()
        {
            var m = R(1, 100, 100, "M", "18-29");
            var stray = R(2, 110, 110, "Male", "18-29");   // real category, stray gender → no scope

            RankCalculator.AssignRanks(new[] { m, stray }, overallBasis: true, categoryBasis: true);

            Assert.AreEqual(1, m.CategoryRank);
            Assert.IsNull(stray.CategoryRank, "gender-scoped category needs a canonical M/F gender");
            Assert.IsNotNull(stray.OverallRank, "still ranked overall");
        }

        // ─── Invariant: a rank can never exceed the size of the population it is ranked
        //     within ("16 of 3" must be impossible). Checked on all six columns. ───

        [TestMethod]
        public void Invariant_NoRankExceedsItsPopulation()
        {
            // Messy roster: both genders, strays, several brackets, ties, null times.
            var roster = new[]
            {
                R(1, 100, 100, "M", "18-29"), R(2, 100, 100, "M", "18-29"),
                R(3, 110, 120, "F", "18-29"), R(4, 110, 120, "F", "18-29"),
                R(5, 130, 110, "F", "30-39"), R(6, 140, 140, "M", "30-39"),
                R(7, 150, 150, "Male", "18-29"), R(8, null, 160, "F", null),
                R(9, 170, null, "M", "Unknown"), R(10, 180, 180, null, "40-49"),
            };

            RankCalculator.AssignRanks(roster, overallBasis: true, categoryBasis: false);

            foreach (var r in roster)
            {
                Assert.IsTrue((r.NetOverallRank ?? 0) <= roster.Length);
                Assert.IsTrue((r.GunOverallRank ?? 0) <= roster.Length);

                var genderCount = roster.Count(x => x.Participant.Gender == r.Participant.Gender);
                Assert.IsTrue((r.NetGenderRank ?? 0) <= genderCount,
                    $"pid {r.ParticipantId}: net gender rank {r.NetGenderRank} > population {genderCount}");
                Assert.IsTrue((r.GunGenderRank ?? 0) <= genderCount);

                var bracketCount = roster.Count(x => x.Participant.Gender == r.Participant.Gender &&
                                                     x.Participant.AgeCategory == r.Participant.AgeCategory);
                Assert.IsTrue((r.NetCategoryRank ?? 0) <= bracketCount,
                    $"pid {r.ParticipantId}: net category rank {r.NetCategoryRank} > gender-scoped population {bracketCount}");
                Assert.IsTrue((r.GunCategoryRank ?? 0) <= bracketCount,
                    $"pid {r.ParticipantId}: gun category rank {r.GunCategoryRank} > gender-scoped population {bracketCount}");
            }
        }

        // ─── UN-DSQ: a restored finisher re-enters the set; everyone below steps back down ───

        [TestMethod]
        public void UnDsq_RestoredFinisher_ShiftsEveryoneBelowBackDown()
        {
            // While X is DSQ'd it is excluded from the finished set — A/B/C rank 1-2-3.
            var a = R(1, net: 100, gun: 100);
            var b = R(2, net: 200, gun: 200);
            var c = R(3, net: 300, gun: 300);
            RankCalculator.AssignRanks(new[] { a, b, c }, overallBasis: true, categoryBasis: true);
            Assert.AreEqual(1, a.OverallRank);
            Assert.AreEqual(2, b.OverallRank);
            Assert.AreEqual(3, c.OverallRank);

            // Clearing the DSQ recomputes X to Finished — the race-wide re-rank includes it
            // again and everyone below its time steps back down (the mirror of the DSQ apply).
            var x = R(4, net: 150, gun: 150);
            RankCalculator.AssignRanks(new[] { a, b, c, x }, overallBasis: true, categoryBasis: true);
            Assert.AreEqual(1, a.OverallRank);
            Assert.AreEqual(2, x.OverallRank, "restored runner slots in by time");
            Assert.AreEqual(3, b.OverallRank, "…and everyone below steps back down");
            Assert.AreEqual(4, c.OverallRank);
        }

        // ─── GenderRank follows the OVERALL basis ───

        [TestMethod]
        public void GenderRank_FollowsOverallBasis()
        {
            var a = R(1, net: 100, gun: 300, "M");
            var b = R(2, net: 300, gun: 100, "M");

            RankCalculator.AssignRanks(new[] { a, b }, overallBasis: false, categoryBasis: true);

            Assert.AreEqual(2, a.GenderRank, "gun basis: b is faster");
            Assert.AreEqual(1, b.GenderRank);
        }

        // ─── ResolveBasis: race/event settings → one authoritative (overall, category) pair ───

        [TestMethod]
        public void ResolveBasis_NoLeaderboardSettings_UsesRankOnNetDefault()
        {
            Assert.AreEqual((true, true), RankCalculator.ResolveBasis(null, rankOnNetDefault: true));
            Assert.AreEqual((false, false), RankCalculator.ResolveBasis(null, rankOnNetDefault: false));
        }

        [TestMethod]
        public void ResolveBasis_PerViewFlags_OverrideDefault()
        {
            var effective = new LeaderboardSettings
            {
                SortByOverallChipTime = false,
                SortByCategoryChipTime = true
            };

            Assert.AreEqual((false, true), RankCalculator.ResolveBasis(effective, rankOnNetDefault: true));
        }

        [TestMethod]
        public void ResolveBasis_NullFlags_FallBackToDefaultPerView()
        {
            var effective = new LeaderboardSettings
            {
                SortByOverallChipTime = true,
                SortByCategoryChipTime = null    // absent per-view flag → event default
            };

            Assert.AreEqual((true, false), RankCalculator.ResolveBasis(effective, rankOnNetDefault: false));
        }
    }
}
