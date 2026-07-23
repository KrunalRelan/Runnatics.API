-- =============================================
-- Script: NotificationLogs_AlignToEntity_20260718.sql
-- Purpose: Reconcile the existing NotificationLogs table (created 2026-05-10)
--          to the current NotificationLog entity so RaceNotificationService.LogAsync
--          can INSERT. All changes are widening / additive — no data loss.
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written idempotent SQL only.
-- Note: Entity NotificationLog.Id is 'long' to match the table's bigint identity.
-- =============================================

BEGIN TRANSACTION;

-- 1. Add missing Success column (code sets it on every log write)
IF COL_LENGTH('dbo.NotificationLogs', 'Success') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationLogs]
        ADD [Success] BIT NOT NULL CONSTRAINT [DF_NotificationLogs_Success] DEFAULT (0);
    PRINT 'Added column: Success';
END
ELSE PRINT 'Column already exists: Success';
GO

-- 2. Relax ParticipantId / RaceId to NULL (support-ticket logs have neither)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationLogs')
           AND name = 'ParticipantId' AND is_nullable = 0)
    ALTER TABLE [dbo].[NotificationLogs] ALTER COLUMN [ParticipantId] INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationLogs')
           AND name = 'RaceId' AND is_nullable = 0)
    ALTER TABLE [dbo].[NotificationLogs] ALTER COLUMN [RaceId] INT NULL;
GO

-- 3. Relax columns the entity does not map to NULL
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationLogs')
           AND name = 'TenantId' AND is_nullable = 0)
    ALTER TABLE [dbo].[NotificationLogs] ALTER COLUMN [TenantId] INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationLogs')
           AND name = 'MessageBody' AND is_nullable = 0)
    ALTER TABLE [dbo].[NotificationLogs] ALTER COLUMN [MessageBody] NVARCHAR(MAX) NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NotificationLogs')
           AND name = 'Status' AND is_nullable = 0)
    ALTER TABLE [dbo].[NotificationLogs] ALTER COLUMN [Status] NVARCHAR(40) NULL;
GO

-- 4. Defaults on unmapped NOT-NULL audit columns so EF's INSERT (which omits them) succeeds
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.NotificationLogs') AND c.name = 'CreatedAt')
    ALTER TABLE [dbo].[NotificationLogs]
        ADD CONSTRAINT [DF_NotificationLogs_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.NotificationLogs') AND c.name = 'IsActive')
    ALTER TABLE [dbo].[NotificationLogs]
        ADD CONSTRAINT [DF_NotificationLogs_IsActive] DEFAULT (1) FOR [IsActive];

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.NotificationLogs') AND c.name = 'IsDeleted')
    ALTER TABLE [dbo].[NotificationLogs]
        ADD CONSTRAINT [DF_NotificationLogs_IsDeleted] DEFAULT (0) FOR [IsDeleted];

PRINT 'NotificationLogs aligned to entity.';
COMMIT;
GO
