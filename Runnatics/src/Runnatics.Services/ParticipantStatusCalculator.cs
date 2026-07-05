using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Constants;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// SINGLE-RUNNER status classification from STORED crossings (the RankCalculator pattern:
    /// one static implementation both interactive paths share, so classification can never fork).
    ///
    /// BUG-26 + #7: mandatory evaluation is per-DISTANCE via the shared
    /// ResultClassifier.MandatoryDistances ({START gate, implicitly} ∪ {IsMandatory} ∪
    /// {finish fallback}); the START gate counts only with an IN-WINDOW crossing
    /// (StartWindow.Contains). all gates valid → Finished · some → DNF · none → DNS.
    ///
    /// Callers: ResultsService (single-runner reprocess) and ParticipantImportService
    /// (UN-DSQ — clearing a disqualification reclassifies from gate coverage, never from
    /// operator choice). The full-race pipeline (Phase 3) keeps its batch-loaded equivalent;
    /// both feed the same ResultClassifier truth table.
    /// </summary>
    public static class ParticipantStatusCalculator
    {
        public static async Task<string> ComputeAsync(
            IUnitOfWork<RaceSyncDbContext> repository, int eventId, int raceId, int participantId)
        {
            var allCheckpoints = await repository.GetRepository<Checkpoint>()
                .GetQuery(cp => cp.RaceId == raceId
                             && cp.EventId == eventId
                             && cp.AuditProperties.IsActive
                             && !cp.AuditProperties.IsDeleted)
                .AsNoTracking()
                .ToListAsync();

            if (allCheckpoints.Count == 0)
                return ResultStatus.DNF;

            var mandatoryDistances = ResultClassifier.MandatoryDistances(allCheckpoints);
            var startGateDistance = allCheckpoints.Min(cp => cp.DistanceFromStart);

            var idsByMandatoryDistance = mandatoryDistances.ToDictionary(
                d => d,
                d => allCheckpoints.Where(cp => cp.DistanceFromStart == d).Select(cp => cp.Id).ToHashSet());

            var gateIds = idsByMandatoryDistance.Values.SelectMany(ids => ids).Distinct().ToList();

            var race = await repository.GetRepository<Race>()
                .GetQuery(r => r.Id == raceId && r.EventId == eventId)
                .Include(r => r.RaceSettings)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            var (windowFloor, windowCeiling) = StartWindow.For(
                race?.StartTime, race?.RaceSettings?.EarlyStartCutOff, race?.RaceSettings?.LateStartCutOff);

            // FINISH CEILING (Races.EndTime): a stored finish-gate crossing after EndTime is not
            // valid data (#7 → gate empty → DNF). Null = feature OFF (EndTime null / sanity guard
            // — the reprocess paths log the warning; this helper stays quiet). Finish-only scope,
            // gate-parameterized: widen by making every gate id a "ceiling gate".
            var finishCeiling = StartWindow.FinishCeiling(race?.StartTime, race?.EndTime);
            var physicalFinishDistance = allCheckpoints.Max(cp => cp.DistanceFromStart);
            var ceilingGateIds = allCheckpoints
                .Where(cp => cp.DistanceFromStart == physicalFinishDistance)
                .Select(cp => cp.Id)
                .ToHashSet();

            var detections = await repository.GetRepository<ReadNormalized>()
                .GetQuery(rn => rn.ParticipantId == participantId
                             && gateIds.Contains(rn.CheckpointId)
                             && !rn.AuditProperties.IsDeleted)
                .Select(rn => new { rn.CheckpointId, rn.ChipTime })
                .ToListAsync();

            var detectedIds = detections
                .Where(d => !(finishCeiling.HasValue &&
                              ceilingGateIds.Contains(d.CheckpointId) &&
                              !StartWindow.WithinCeiling(d.ChipTime, finishCeiling)))
                .Select(d => d.CheckpointId)
                .ToHashSet();
            var startGateIds = idsByMandatoryDistance[startGateDistance];
            var startChip = detections
                .Where(d => startGateIds.Contains(d.CheckpointId))
                .OrderBy(d => d.ChipTime)
                .Select(d => (DateTime?)d.ChipTime)
                .FirstOrDefault();

            var validGates = 0;
            foreach (var d in mandatoryDistances)
            {
                var gateValid = d == startGateDistance
                    ? startChip.HasValue && StartWindow.Contains(startChip.Value, windowFloor, windowCeiling)
                    : idsByMandatoryDistance[d].Overlaps(detectedIds);
                if (gateValid)
                    validGates++;
            }

            return ResultClassifier.Classify(validGates, mandatoryDistances.Count) switch
            {
                ParticipantOutcome.Finished => ResultStatus.Finished,
                ParticipantOutcome.DNF => ResultStatus.DNF,
                _ => ResultStatus.DNS
            };
        }
    }
}
