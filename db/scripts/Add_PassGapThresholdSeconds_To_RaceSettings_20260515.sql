-- Add PassGapThresholdSeconds to RaceSettings.
-- Controls the minimum gap (seconds) between RFID readings at a shared mat
-- that triggers a new pass detection in the loop-race checkpoint assigner.
-- Default in code: 300 (5 minutes). NULL = use code default.

ALTER TABLE dbo.RaceSettings
ADD PassGapThresholdSeconds INT NULL;
