-- One-time fix: set IsMandatory = 1 on all Checkpoints that have IsMandatory = 0 or NULL.
-- Run this BEFORE re-running result calculation for any race.
-- Safe to run multiple times (WHERE guard makes it idempotent).

UPDATE dbo.Checkpoints
SET IsMandatory = 1
WHERE IsMandatory IS NULL OR IsMandatory = 0;

-- Confirm
SELECT
    COUNT(*)        AS TotalCheckpoints,
    SUM(CASE WHEN IsMandatory = 1 THEN 1 ELSE 0 END) AS MandatoryCount
FROM dbo.Checkpoints;
