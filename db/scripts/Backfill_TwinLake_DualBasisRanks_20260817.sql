-- ############################################################################
-- SUPERSEDED — DO NOT RUN.
-- This script predates the gender-scoped category decision: it partitions
-- category ranks by AgeCategory alone and will write MIXED-GENDER category
-- ranks. Use Backfill_DualBasisRanks_GenderScopedCategory_20260818.sql.
-- The guard below makes this script refuse to execute.
-- ############################################################################
RAISERROR('SUPERSEDED: use Backfill_DualBasisRanks_GenderScopedCategory_20260818.sql', 16, 1);
RETURN;

-- One-off backfill: dual-basis rank sets for Twin Lake Ultra Edition 2, 14.5 Km.
-- Reproduces RankCalculator.AssignRanks exactly:
--   * shared competition numbering (RANK() = 1,2,2,4)
--   * NULL times sort last and never share a rank (each gets its own ordinal)
--   * GenderRank only for canonical 'M'/'F'; CategoryRank only for real (non-Unknown) categories
--   * legacy OverallRank/GenderRank/CategoryRank = the configured-basis set
--     (basis resolved the same way as RankCalculator.ResolveBasis:
--      race-override LeaderboardSettings -> event-level -> EventSettings.RankOnNet)
-- Idempotent / re-runnable: recomputes from stored NetTime/GunTime every run.
-- Take a backup first. To also backfill the 50 Km race (recommended for event-wide
-- consistency), re-run with the @Distance value changed to 50.

DECLARE @Distance DECIMAL(9,2) = 14.5;

DECLARE @RaceId INT = (
    SELECT TOP 1 r.Id
    FROM Races r JOIN Events e ON e.Id = r.EventId
    WHERE e.Name LIKE '%Twin Lake%' AND r.Distance = @Distance
      AND r.IsActive = 1 AND r.IsDeleted = 0);

IF @RaceId IS NULL BEGIN RAISERROR('Race not found', 16, 1); RETURN; END;

DECLARE @EventId INT = (SELECT EventId FROM Races WHERE Id = @RaceId);

-- Effective basis: race override -> event-level -> RankOnNet -> gun (0). 1 = chip/net.
DECLARE @OverallNet BIT = COALESCE(
    (SELECT TOP 1 SortByOverallChipTime FROM LeaderboardSettings
     WHERE EventId = @EventId AND RaceId = @RaceId AND OverrideSettings = 1 AND IsActive = 1 AND IsDeleted = 0),
    (SELECT TOP 1 SortByOverallChipTime FROM LeaderboardSettings
     WHERE EventId = @EventId AND RaceId IS NULL AND OverrideSettings = 0 AND IsActive = 1 AND IsDeleted = 0),
    (SELECT TOP 1 RankOnNet FROM EventSettings
     WHERE EventId = @EventId AND IsActive = 1 AND IsDeleted = 0),
    0);
DECLARE @CategoryNet BIT = COALESCE(
    (SELECT TOP 1 SortByCategoryChipTime FROM LeaderboardSettings
     WHERE EventId = @EventId AND RaceId = @RaceId AND OverrideSettings = 1 AND IsActive = 1 AND IsDeleted = 0),
    (SELECT TOP 1 SortByCategoryChipTime FROM LeaderboardSettings
     WHERE EventId = @EventId AND RaceId IS NULL AND OverrideSettings = 0 AND IsActive = 1 AND IsDeleted = 0),
    (SELECT TOP 1 RankOnNet FROM EventSettings
     WHERE EventId = @EventId AND IsActive = 1 AND IsDeleted = 0),
    0);

SELECT @RaceId AS RaceId, @OverallNet AS OverallBasisIsNet, @CategoryNet AS CategoryBasisIsNet;

;WITH F AS (
    SELECT res.Id, res.ParticipantId, res.NetTime, res.GunTime,
           p.Gender, p.AgeCategory,
           CASE WHEN p.Gender IN ('M','F') THEN 1 ELSE 0 END AS GenderRanked,
           CASE WHEN p.AgeCategory IS NOT NULL AND LTRIM(RTRIM(p.AgeCategory)) <> ''
                 AND LOWER(p.AgeCategory) <> 'unknown' THEN 1 ELSE 0 END AS CatRanked
    FROM Results res
    JOIN Participants p ON p.Id = res.ParticipantId
    WHERE res.RaceId = @RaceId AND res.Status = 'Finished'
      AND res.IsActive = 1 AND res.IsDeleted = 0
),
Ranked AS (
    SELECT Id, GenderRanked, CatRanked,
        RANK() OVER (ORDER BY CASE WHEN NetTime IS NULL THEN 1 ELSE 0 END, NetTime,
                     CASE WHEN NetTime IS NULL THEN ParticipantId END) AS NetOverall,
        RANK() OVER (ORDER BY CASE WHEN GunTime IS NULL THEN 1 ELSE 0 END, GunTime,
                     CASE WHEN GunTime IS NULL THEN ParticipantId END) AS GunOverall,
        RANK() OVER (PARTITION BY CASE WHEN GenderRanked = 1 THEN Gender END
                     ORDER BY CASE WHEN NetTime IS NULL THEN 1 ELSE 0 END, NetTime,
                     CASE WHEN NetTime IS NULL THEN ParticipantId END) AS NetGender,
        RANK() OVER (PARTITION BY CASE WHEN GenderRanked = 1 THEN Gender END
                     ORDER BY CASE WHEN GunTime IS NULL THEN 1 ELSE 0 END, GunTime,
                     CASE WHEN GunTime IS NULL THEN ParticipantId END) AS GunGender,
        RANK() OVER (PARTITION BY CASE WHEN CatRanked = 1 THEN AgeCategory END
                     ORDER BY CASE WHEN NetTime IS NULL THEN 1 ELSE 0 END, NetTime,
                     CASE WHEN NetTime IS NULL THEN ParticipantId END) AS NetCat,
        RANK() OVER (PARTITION BY CASE WHEN CatRanked = 1 THEN AgeCategory END
                     ORDER BY CASE WHEN GunTime IS NULL THEN 1 ELSE 0 END, GunTime,
                     CASE WHEN GunTime IS NULL THEN ParticipantId END) AS GunCat
    FROM F
)
UPDATE res SET
    NetOverallRank  = rk.NetOverall,
    NetGenderRank   = CASE WHEN rk.GenderRanked = 1 THEN rk.NetGender END,
    NetCategoryRank = CASE WHEN rk.CatRanked = 1 THEN rk.NetCat END,
    GunOverallRank  = rk.GunOverall,
    GunGenderRank   = CASE WHEN rk.GenderRanked = 1 THEN rk.GunGender END,
    GunCategoryRank = CASE WHEN rk.CatRanked = 1 THEN rk.GunCat END,
    OverallRank  = CASE WHEN @OverallNet = 1 THEN rk.NetOverall ELSE rk.GunOverall END,
    GenderRank   = CASE WHEN rk.GenderRanked = 0 THEN NULL
                        WHEN @OverallNet = 1 THEN rk.NetGender ELSE rk.GunGender END,
    CategoryRank = CASE WHEN rk.CatRanked = 0 THEN NULL
                        WHEN @CategoryNet = 1 THEN rk.NetCat ELSE rk.GunCat END,
    UpdatedAt = GETUTCDATE()
FROM Results res
JOIN Ranked rk ON rk.Id = res.Id;

-- ── Verification ─────────────────────────────────────────────────────────────

-- V1: bibs 1005 / 1314. Expected: 1005 chip 55, gun 54; 1314 gun 54 (shared).
SELECT p.BibNumber, res.NetOverallRank, res.NetGenderRank, res.NetCategoryRank,
       res.GunOverallRank, res.GunGenderRank, res.GunCategoryRank,
       res.OverallRank AS LegacyOverall
FROM Results res JOIN Participants p ON p.Id = res.ParticipantId
WHERE res.RaceId = @RaceId AND p.BibNumber IN ('1005','1314')
  AND res.IsActive = 1 AND res.IsDeleted = 0;

-- V2: the 4-way gun tie group — all four must share one GunOverallRank,
-- and the next finisher must be at that rank + 4.
SELECT p.BibNumber, res.GunTime, res.GunOverallRank
FROM Results res JOIN Participants p ON p.Id = res.ParticipantId
WHERE res.RaceId = @RaceId AND res.Status = 'Finished'
  AND res.IsActive = 1 AND res.IsDeleted = 0
  AND res.GunTime IN (SELECT GunTime FROM Results
                      WHERE RaceId = @RaceId AND Status = 'Finished'
                        AND IsActive = 1 AND IsDeleted = 0
                      GROUP BY GunTime HAVING COUNT(*) = 4)
ORDER BY res.GunOverallRank;

-- V3: rank-sequence sanity on both bases — every gap must equal the size of the
-- tie group that precedes it (competition numbering); no duplicates outside ties.
SELECT GunOverallRank, COUNT(*) AS Runners
FROM Results WHERE RaceId = @RaceId AND Status = 'Finished' AND IsActive = 1 AND IsDeleted = 0
GROUP BY GunOverallRank ORDER BY GunOverallRank;

SELECT NetOverallRank, COUNT(*) AS Runners
FROM Results WHERE RaceId = @RaceId AND Status = 'Finished' AND IsActive = 1 AND IsDeleted = 0
GROUP BY NetOverallRank ORDER BY NetOverallRank;
