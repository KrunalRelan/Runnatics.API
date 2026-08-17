-- Dual-basis rank sets on Results (explicit pairs).
-- Net* = ranks computed on chip (net) time; Gun* = ranks computed on gun time.
-- Both sets use shared competition numbering (1,2,2,4). The legacy
-- OverallRank/GenderRank/CategoryRank columns keep the configured-basis values
-- (see TECH_DEBT.md — legacy set is now redundant and should be retired once
-- all consumers read the explicit pairs).
-- Idempotent: safe to re-run.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Results') AND name = 'NetOverallRank')
    ALTER TABLE dbo.Results ADD NetOverallRank INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Results') AND name = 'NetGenderRank')
    ALTER TABLE dbo.Results ADD NetGenderRank INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Results') AND name = 'NetCategoryRank')
    ALTER TABLE dbo.Results ADD NetCategoryRank INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Results') AND name = 'GunOverallRank')
    ALTER TABLE dbo.Results ADD GunOverallRank INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Results') AND name = 'GunGenderRank')
    ALTER TABLE dbo.Results ADD GunGenderRank INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Results') AND name = 'GunCategoryRank')
    ALTER TABLE dbo.Results ADD GunCategoryRank INT NULL;
