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
    ///   Tiebreak (deterministic + stable across reprocess): primary time -> other time -> ParticipantId.
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
            // Overall — by the overall basis.
            var overall = OrderByBasis(finished, overallBasis).ToList();
            for (int i = 0; i < overall.Count; i++)
                overall[i].OverallRank = i + 1;

            // Gender — by the overall basis, ONLY for canonical "M"/"F". Any other / stray / empty
            // gender value gets a NULL GenderRank (still ranked Overall + Category) so a typo or a
            // non-M/F string can't form a phantom rank-of-1 group. A legitimate third gender that
            // should rank is a deliberate decision — add it here explicitly, don't auto-include.
            foreach (var r in finished)
                r.GenderRank = null;
            foreach (var gender in new[] { "M", "F" })
            {
                var rank = 1;
                foreach (var r in OrderByBasis(overall.Where(x => x.Participant?.Gender == gender), overallBasis))
                    r.GenderRank = rank++;
            }

            // Category — by the category basis. Uncategorized / "Unknown" -> null rank (BUG-12).
            foreach (var r in finished)
                r.CategoryRank = null;
            foreach (var categoryGroup in finished
                         .Where(r => HasCategory(r.Participant?.AgeCategory))
                         .GroupBy(r => r.Participant!.AgeCategory!))
            {
                var rank = 1;
                foreach (var r in OrderByBasis(categoryGroup, categoryBasis))
                    r.CategoryRank = rank++;
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
