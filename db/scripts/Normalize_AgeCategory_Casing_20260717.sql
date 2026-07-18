-- Converge AgeCategory CASING per event onto one canonical spelling.
--
-- WHY: Participants.AgeCategory is free text. Inconsistent casing/whitespace
-- ("30 to Under 50 Yrs" vs "... yrs") fragments a logical category into two buckets.
-- Because stored Results.CategoryRank is computed from the raw string
-- (RankCalculator.AssignRanks groups case-sensitively), a split category yields TWO
-- category winners and splits its runners across spellings.
--
-- This script picks, per (event, case-insensitive category), the MOST COMMON existing
-- casing as canonical and rewrites the others to match. "Unknown"/blank are left alone.
--
-- Idempotent: only rows not already canonical are updated; re-running is a no-op.
--
-- ⚠️ AFTER RUNNING PART 2: every affected race MUST be re-ranked so CategoryRank
--    recomputes over the now-unified category (RankCalculator.ApplyStoredRanksAsync,
--    e.g. via the app's re-rank/results-refresh path). Some affected events may be
--    PUBLISHED — review Part 1 output first to know the blast radius.

------------------------------------------------------------------------------------
-- PART 1 — BLAST RADIUS (run first; review before Part 2). Read-only.
------------------------------------------------------------------------------------

-- 1a. Categories that exist in more than one casing, per event.
;WITH Variants AS (
    SELECT p.EventId,
           LOWER(LTRIM(RTRIM(p.AgeCategory))) AS CatKey,
           LTRIM(RTRIM(p.AgeCategory))        AS CatValue,
           COUNT(*)                           AS Cnt
    FROM dbo.Participants p
    WHERE p.IsActive = 1 AND p.IsDeleted = 0
      AND p.AgeCategory IS NOT NULL
      AND LTRIM(RTRIM(p.AgeCategory)) <> ''
      AND LOWER(LTRIM(RTRIM(p.AgeCategory))) <> 'unknown'
    GROUP BY p.EventId, LOWER(LTRIM(RTRIM(p.AgeCategory))), LTRIM(RTRIM(p.AgeCategory))
)
SELECT EventId,
       CatKey,
       COUNT(*)  AS CasingVariants,
       SUM(Cnt)  AS Participants
FROM Variants
GROUP BY EventId, CatKey
HAVING COUNT(*) > 1
ORDER BY EventId, CatKey;

-- 1b. Races that need re-ranking after Part 2 (those in an affected event whose
--     category has >1 casing). Feed these EventId/RaceId pairs into the re-rank path.
;WITH MultiCasing AS (
    SELECT p.EventId, LOWER(LTRIM(RTRIM(p.AgeCategory))) AS CatKey
    FROM dbo.Participants p
    WHERE p.IsActive = 1 AND p.IsDeleted = 0
      AND p.AgeCategory IS NOT NULL
      AND LTRIM(RTRIM(p.AgeCategory)) <> ''
      AND LOWER(LTRIM(RTRIM(p.AgeCategory))) <> 'unknown'
    GROUP BY p.EventId, LOWER(LTRIM(RTRIM(p.AgeCategory)))
    HAVING COUNT(DISTINCT LTRIM(RTRIM(p.AgeCategory))) > 1
)
SELECT DISTINCT p.EventId, p.RaceId
FROM dbo.Participants p
JOIN MultiCasing m
  ON m.EventId = p.EventId
 AND m.CatKey  = LOWER(LTRIM(RTRIM(p.AgeCategory)))
WHERE p.IsActive = 1 AND p.IsDeleted = 0
ORDER BY p.EventId, p.RaceId;

------------------------------------------------------------------------------------
-- PART 2 — CONVERGENCE UPDATE. Mutates data. Run after reviewing Part 1.
------------------------------------------------------------------------------------
;WITH Ranked AS (
    SELECT p.EventId,
           LOWER(LTRIM(RTRIM(p.AgeCategory))) AS CatKey,
           LTRIM(RTRIM(p.AgeCategory))        AS CatValue,
           COUNT(*)                           AS Cnt
    FROM dbo.Participants p
    WHERE p.IsActive = 1 AND p.IsDeleted = 0
      AND p.AgeCategory IS NOT NULL
      AND LTRIM(RTRIM(p.AgeCategory)) <> ''
      AND LOWER(LTRIM(RTRIM(p.AgeCategory))) <> 'unknown'
    GROUP BY p.EventId, LOWER(LTRIM(RTRIM(p.AgeCategory))), LTRIM(RTRIM(p.AgeCategory))
),
Canonical AS (
    SELECT EventId, CatKey, CatValue,
           ROW_NUMBER() OVER (PARTITION BY EventId, CatKey
                              ORDER BY Cnt DESC, CatValue ASC) AS rn
    FROM Ranked
)
UPDATE p
SET p.AgeCategory = c.CatValue,
    p.UpdatedAt   = GETUTCDATE()
FROM dbo.Participants p
JOIN Canonical c
  ON c.EventId = p.EventId
 AND c.CatKey  = LOWER(LTRIM(RTRIM(p.AgeCategory)))
 AND c.rn = 1
WHERE p.IsActive = 1 AND p.IsDeleted = 0
  AND p.AgeCategory IS NOT NULL
  AND p.AgeCategory <> c.CatValue;   -- idempotent: skip rows already canonical
