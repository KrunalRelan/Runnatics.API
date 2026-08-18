using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// SINGLE SOURCE OF TRUTH for finisher ranking. Both the reprocess pipeline
    /// (RFIDImportService) and the interactive path (ResultsService) call this, so the STORED
    /// OverallRank / GenderRank / CategoryRank are computed once with one basis — and the admin
    /// grid, public site, and export (which all read the stored ranks) can never disagree.
    ///
    /// Assigns ranks IN PLACE on the supplied FINISHED results only (DNF/DNS/DSQ keep null ranks —
    /// the caller must exclude them). Each result's Participant must be loaded (Gender / AgeCategory).
    ///
    ///   overallBasis / categoryBasis: true = rank by CHIP (net) time, false = GUN time.
    ///   GenderRank follows the overall basis.
    ///   CategoryRank is scoped to (Gender, AgeCategory) — men and women rank separately within
    ///   each age bracket (2026-08 client decision); requires canonical M/F gender.
    ///   Every run populates BOTH explicit sets (Net* / Gun*) plus the legacy columns
    ///   (configured basis). Numbering is SHARED-COMPETITION (1,2,2,4): equal primary times share
    ///   a rank; the next distinct time resumes at its ordinal position (2026-08 client decision).
    ///   Within a tie group the storage/display order stays deterministic:
    ///   primary time -> other time -> ParticipantId.
    ///   (ParticipantId, not Bib: bibs are reused/non-unique and are strings — see project_bib_not_unique.)
    /// </summary>
    public static class RankCalculator
    {
        /// <summary>
        /// Resolves the (overall, category) ranking basis from the EFFECTIVE leaderboard settings
        /// (race-level override else event-level), defaulting to EventSettings.RankOnNet when a
        /// per-view flag is absent. true = CHIP (net), false = GUN. This is the ONE place the basis
        /// is decided, so the per-view (SortByOverallChipTime / SortByCategoryChipTime) and the
        /// event-level (RankOnNet) settings are reconciled into a single authoritative answer.
        /// </summary>
        public static (bool Overall, bool Category) ResolveBasis(LeaderboardSettings? effective, bool rankOnNetDefault)
            => (effective?.SortByOverallChipTime ?? rankOnNetDefault,
                effective?.SortByCategoryChipTime ?? rankOnNetDefault);

        /// <summary>
        /// THE one stored-rank entry point. Loads this race's FINISHED results (+ Participant),
        /// resolves the basis from effective leaderboard settings (race override else event-level,
        /// default EventSettings.RankOnNet), assigns ranks via <see cref="AssignRanks"/>, and persists.
        /// Both calc paths (reprocess pipeline + interactive manual edit) call this — so stored ranks
        /// are identical regardless of which path ran, and every display surface just reads them.
        /// </summary>
        public static async Task ApplyStoredRanksAsync(
            IUnitOfWork<RaceSyncDbContext> repository, int eventId, int raceId, int? userId)
        {
            var resultsRepo = repository.GetRepository<Results>();
            var finished = await resultsRepo.GetQuery(r =>
                    r.EventId == eventId && r.RaceId == raceId &&
                    r.Status == "Finished" &&
                    r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                .Include(r => r.Participant)
                .AsNoTracking()
                .ToListAsync();
            if (finished.Count == 0)
                return;

            // Effective leaderboard settings: race-level override, else event-level. (Mirrors
            // RaceService.GetEffectiveLeaderboardSettings.)
            var lbRepo = repository.GetRepository<LeaderboardSettings>();
            var effective = await lbRepo.GetQuery(ls =>
                    ls.EventId == eventId && ls.RaceId == raceId && ls.OverrideSettings == true &&
                    ls.AuditProperties.IsActive && !ls.AuditProperties.IsDeleted)
                .AsNoTracking().FirstOrDefaultAsync()
                ?? await lbRepo.GetQuery(ls =>
                    ls.EventId == eventId && ls.RaceId == null && ls.OverrideSettings == false &&
                    ls.AuditProperties.IsActive && !ls.AuditProperties.IsDeleted)
                .AsNoTracking().FirstOrDefaultAsync();

            var eventSettings = await repository.GetRepository<EventSettings>().GetQuery(es =>
                    es.EventId == eventId &&
                    es.AuditProperties.IsActive && !es.AuditProperties.IsDeleted)
                .AsNoTracking().FirstOrDefaultAsync();

            var (overallBasis, categoryBasis) = ResolveBasis(effective, eventSettings?.RankOnNet ?? false);
            AssignRanks(finished, overallBasis, categoryBasis);

            foreach (var r in finished)
            {
                r.AuditProperties.UpdatedBy = userId;
                r.AuditProperties.UpdatedDate = DateTime.UtcNow;
            }
            await resultsRepo.BulkUpdateAsync(finished);
        }

        public static void AssignRanks(IReadOnlyCollection<Results> finished, bool overallBasis, bool categoryBasis)
        {
            // Explicit dual-basis sets — every rank run populates all six columns so both bases
            // are always available regardless of the configured basis.
            AssignBasisSet(finished, net: true,
                (r, v) => r.NetOverallRank = v, (r, v) => r.NetGenderRank = v, (r, v) => r.NetCategoryRank = v);
            AssignBasisSet(finished, net: false,
                (r, v) => r.GunOverallRank = v, (r, v) => r.GunGenderRank = v, (r, v) => r.GunCategoryRank = v);

            // Legacy single-basis columns — copy from the explicit set matching the configured
            // basis, so every current consumer (leaderboard, podium, export, certificates, SMS)
            // keeps reading the basis it always has. Numbering is now shared-competition (1,2,2,4)
            // per the 2026-08 client decision. See TECH_DEBT.md: retire these once all consumers
            // read the explicit pairs.
            foreach (var r in finished)
            {
                r.OverallRank  = overallBasis  ? r.NetOverallRank  : r.GunOverallRank;
                r.GenderRank   = overallBasis  ? r.NetGenderRank   : r.GunGenderRank;
                r.CategoryRank = categoryBasis ? r.NetCategoryRank : r.GunCategoryRank;
            }
        }

        // Computes one full basis set (overall / gender / category) with shared competition
        // numbering: runners with an identical primary time share a rank, and the next distinct
        // time resumes at its ordinal position (1,2,2,4).
        private static void AssignBasisSet(
            IReadOnlyCollection<Results> finished, bool net,
            Action<Results, int?> setOverall, Action<Results, int?> setGender, Action<Results, int?> setCategory)
        {
            AssignSharedRanks(finished, net, setOverall);

            // Gender — ONLY for canonical "M"/"F". Any other / stray / empty gender value gets a
            // NULL rank (still ranked Overall + Category) so a typo or a non-M/F string can't form
            // a phantom rank-of-1 group. A legitimate third gender that should rank is a deliberate
            // decision — add it here explicitly, don't auto-include.
            foreach (var r in finished)
                setGender(r, null);
            foreach (var gender in new[] { "M", "F" })
                AssignSharedRanks(finished.Where(x => x.Participant?.Gender == gender), net, setGender);

            // Category — scoped to (Gender, AgeCategory): men and women rank separately within
            // each age bracket (2026-08 client decision). Requires a canonical M/F gender AND a
            // real category; strays / uncategorized / "Unknown" -> null rank (BUG-12), still
            // ranked Overall.
            foreach (var r in finished)
                setCategory(r, null);
            foreach (var categoryGroup in finished
                         .Where(r => (r.Participant?.Gender == "M" || r.Participant?.Gender == "F") &&
                                     HasCategory(r.Participant?.AgeCategory))
                         .GroupBy(r => new { r.Participant!.Gender, r.Participant.AgeCategory }))
                AssignSharedRanks(categoryGroup, net, setCategory);
        }

        // Shared competition numbering over one ordered population: equal primary times share the
        // first tied position's rank; a NULL primary time never shares (each gets its own ordinal).
        private static void AssignSharedRanks(IEnumerable<Results> items, bool net, Action<Results, int?> set)
        {
            var ordered = OrderByBasis(items, net).ToList();
            long? prevKey = null;
            int rank = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                var key = net ? ordered[i].NetTime : ordered[i].GunTime;
                if (i == 0 || key == null || key != prevKey)
                    rank = i + 1;
                set(ordered[i], rank);
                prevKey = key;
            }
        }

        // primary time asc -> other time asc -> ParticipantId asc (stable, deterministic).
        private static IEnumerable<Results> OrderByBasis(IEnumerable<Results> items, bool net) =>
            net
                ? items.OrderBy(r => r.NetTime ?? long.MaxValue)
                       .ThenBy(r => r.GunTime ?? long.MaxValue)
                       .ThenBy(r => r.ParticipantId)
                : items.OrderBy(r => r.GunTime ?? long.MaxValue)
                       .ThenBy(r => r.NetTime ?? long.MaxValue)
                       .ThenBy(r => r.ParticipantId);

        private static bool HasCategory(string? c) =>
            !string.IsNullOrWhiteSpace(c) && !string.Equals(c, "Unknown", StringComparison.OrdinalIgnoreCase);
    }
}
