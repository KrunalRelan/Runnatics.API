-- =============================================================================
-- Testing Feedback Round 1 — Schema Changes
-- Date: 2026-05-15
-- Script is IDEMPOTENT — safe to re-run multiple times without errors.
-- =============================================================================

-- =============================================================================
-- (1) Participants — ManualDistance column
-- =============================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Participants]') AND name = 'ManualDistance'
)
BEGIN
    ALTER TABLE [dbo].[Participants]
        ADD [ManualDistance] DECIMAL(8,3) NULL;
END
GO

-- (1b) Participants — normalize existing Gender values to M/F
UPDATE [dbo].[Participants]
SET [Gender] = CASE
    WHEN UPPER([Gender]) IN ('M', 'MALE') THEN 'M'
    WHEN UPPER([Gender]) IN ('F', 'FEMALE') THEN 'F'
    ELSE [Gender]
END
WHERE [Gender] NOT IN ('M', 'F', '');
GO

-- =============================================================================
-- (2) Checkpoints — IsMandatory column (default true)
-- =============================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Checkpoints]') AND name = 'IsMandatory'
)
BEGIN
    ALTER TABLE [dbo].[Checkpoints]
        ADD [IsMandatory] BIT NOT NULL
            CONSTRAINT [DF_Checkpoints_IsMandatory] DEFAULT (1);
END
GO

-- =============================================================================
-- (3) Races — IsTimed column (default true)
-- =============================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Races]') AND name = 'IsTimed'
)
BEGIN
    ALTER TABLE [dbo].[Races]
        ADD [IsTimed] BIT NOT NULL
            CONSTRAINT [DF_Races_IsTimed] DEFAULT (1);
END
GO

-- =============================================================================
-- (4) RawRFIDReadings — IsMultipleEpc column (default false)
-- =============================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[RawRFIDReadings]') AND name = 'IsMultipleEpc'
)
BEGIN
    ALTER TABLE [dbo].[RawRFIDReadings]
        ADD [IsMultipleEpc] BIT NOT NULL
            CONSTRAINT [DF_RawRFIDReadings_IsMultipleEpc] DEFAULT (0);
END
GO

-- =============================================================================
-- (5) UploadBatches — Drop unique index/constraint on FileHash (if any)
-- =============================================================================

-- Drop unique INDEX on FileHash
DECLARE @ix sysname;
SELECT @ix = i.name
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id
                          AND i.index_id  = ic.index_id
JOIN sys.columns c        ON ic.object_id = c.object_id
                          AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID(N'[dbo].[UploadBatches]')
  AND c.name = 'FileHash'
  AND i.is_unique = 1;

IF @ix IS NOT NULL
    EXEC('DROP INDEX [' + @ix + '] ON [dbo].[UploadBatches]');
GO

-- Drop unique CONSTRAINT on FileHash (sys.key_constraints variant)
DECLARE @uc sysname;
SELECT @uc = kc.name
FROM sys.key_constraints kc
JOIN sys.index_columns ic ON kc.unique_index_id = ic.index_id
                          AND kc.parent_object_id = ic.object_id
JOIN sys.columns c         ON ic.object_id = c.object_id
                          AND ic.column_id = c.column_id
WHERE kc.parent_object_id = OBJECT_ID(N'[dbo].[UploadBatches]')
  AND kc.type = 'UQ'
  AND c.name = 'FileHash';

IF @uc IS NOT NULL
    EXEC('ALTER TABLE [dbo].[UploadBatches] DROP CONSTRAINT [' + @uc + ']');
GO

-- (5b) UploadBatches — TotalTagsInFile column
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[UploadBatches]') AND name = 'TotalTagsInFile'
)
BEGIN
    ALTER TABLE [dbo].[UploadBatches]
        ADD [TotalTagsInFile] INT NOT NULL
            CONSTRAINT [DF_UploadBatches_TotalTagsInFile] DEFAULT (0);
END
GO

-- (5c) UploadBatches — TagsProcessed column
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[UploadBatches]') AND name = 'TagsProcessed'
)
BEGIN
    ALTER TABLE [dbo].[UploadBatches]
        ADD [TagsProcessed] INT NOT NULL
            CONSTRAINT [DF_UploadBatches_TagsProcessed] DEFAULT (0);
END
GO

-- =============================================================================
-- BUG API-14 — Performance indexes
-- =============================================================================

-- (a) Participants composite index for admin list / search hot path
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Participants]')
      AND name = 'IX_Participants_TenantId_EventId_RaceId_BibNumber'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Participants_TenantId_EventId_RaceId_BibNumber]
        ON [dbo].[Participants] ([TenantId], [EventId], [RaceId], [Bib]);
END
GO

-- (b) RawRFIDReadings index for batch query + time ordering
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[RawRFIDReadings]')
      AND name = 'IX_RawRFIDReadings_BatchId_ReadTimeUtc'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_RawRFIDReadings_BatchId_ReadTimeUtc]
        ON [dbo].[RawRFIDReadings] ([BatchId], [ReadTimeUtc]);
END
GO
