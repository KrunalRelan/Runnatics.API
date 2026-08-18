-- Re-rank races 100, 101, 102 with the gender-scoped category rule.
-- Supersedes Backfill_TwinLake_DualBasisRanks_20260817.sql.
-- Reproduces RankCalculator.AssignRanks exactly:
--   * shared competition numbering (RANK() = 1,2,2,4)
--   * NULL times sort last and never share a rank
--   * GenderRank only for canonical 'M'/'F'
--   * CategoryRank scoped to (Gender, AgeCategory) — men and women rank separately
--     within each age bracket; requires canonical M/F gender AND a real category
--   * legacy OverallRank/GenderRank/CategoryRank = the configured-basis set
-- Idempotent / re-runnable. Backup exists: Results_RankBackup_TwinLake_20260817
-- (keyed on ParticipantId + RaceId; Results.ParticipantId is unique, so a restore
-- joins on ParticipantId alone).

DECLARE @RaceIds TABLE (Id INT PRIMARY KEY);
INSERT INTO @RaceIds VALUES (100), (101), (102);

DECLARE @RaceId INT;
DECLARE raceCur CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM @RaceIds;
OPEN raceCur;
FETCH NEXT FROM raceCur INTO @RaceId;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @EventId INT = (SELECT EventId FROM Races WHERE Id = @RaceId);
    IF @EventId IS NULL
    BEGIN
        PRINT CONCAT('Race ', @RaceId, ' not found - skipped');
        FETCH NEXT FROM raceCur INTO @RaceId;
        CONTINUE;
    END;

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

    PRINT CONCAT('Race ', @RaceId, ': OverallBasisIsNet=', @OverallNet, ', CategoryBasisIsNet=', @CategoryNet);

    ;WITH F AS (
        SELECT res.Id, res.ParticipantId, res.NetTime, res.GunTime,
               p.Gender, p.AgeCategory,
               CASE WHEN p.Gender IN ('M','F') THEN 1 ELSE 0 END AS GenderRanked,
               CASE WHEN p.Gender IN ('M','F')
                     AND p.AgeCategory IS NOT NULL AND LTRIM(RTRIM(p.AgeCategory)) <> ''
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
            -- Gender-scoped category: partition on (Gender, AgeCategory)
            RANK() OVER (PARTITION BY CASE WHEN CatRanked = 1 THEN CONCAT(Gender, '|', AgeCategory) END
                         ORDER BY CASE WHEN NetTime IS NULL THEN 1 ELSE 0 END, NetTime,
                         CASE WHEN NetTime IS NULL THEN ParticipantId END) AS NetCat,
            RANK() OVER (PARTITION BY CASE WHEN CatRanked = 1 THEN CONCAT(Gender, '|', AgeCategory) END
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

    PRINT CONCAT('Race ', @RaceId, ': ', @@ROWCOUNT, ' results re-ranked');

    FETCH NEXT FROM raceCur INTO @RaceId;
END;
CLOSE raceCur; DEALLOCATE raceCur;

-- ── Verification ─────────────────────────────────────────────────────────────

-- V1: bibs 1005 / 1314. Expected: 1005 category 3 of 3 (F 18-35) on both bases;
--     overall/gender unchanged (chip 55/11, gun 54/10 shared with 1314).
SELECT res.RaceId, p.BibNumber, p.Gender, p.AgeCategory,
       res.NetOverallRank, res.NetGenderRank, res.NetCategoryRank,
       res.GunOverallRank, res.GunGenderRank, res.GunCategoryRank
FROM Results res JOIN Participants p ON p.Id = res.ParticipantId
WHERE res.RaceId IN (100,101,102) AND p.BibNumber IN ('1005','1314')
  AND res.IsActive = 1 AND res.IsDeleted = 0;

-- V2: per-bracket per-gender rank sequences — each (gender, bracket) must run 1..N
-- with only competition-numbering gaps; denominator check via the Finishers count.
SELECT res.RaceId, p.Gender, p.AgeCategory,
       COUNT(*) AS Finishers, MIN(res.NetCategoryRank) AS MinRank, MAX(res.NetCategoryRank) AS MaxRank
FROM Results res JOIN Participants p ON p.Id = res.ParticipantId
WHERE res.RaceId IN (100,101,102) AND res.Status = 'Finished'
  AND res.IsActive = 1 AND res.IsDeleted = 0 AND res.NetCategoryRank IS NOT NULL
GROUP BY res.RaceId, p.Gender, p.AgeCategory
ORDER BY res.RaceId, p.Gender, p.AgeCategory;

-- V3: female 18-35 podium spot-check (adjust bracket label to the stored value).
SELECT TOP 5 res.RaceId, p.BibNumber, p.FirstName, p.LastName, res.NetTime, res.NetCategoryRank
FROM Results res JOIN Participants p ON p.Id = res.ParticipantId
WHERE res.RaceId IN (100,101,102) AND p.Gender = 'F' AND p.AgeCategory LIKE '%18%35%'
  AND res.Status = 'Finished' AND res.IsActive = 1 AND res.IsDeleted = 0
ORDER BY res.RaceId, res.NetCategoryRank;
