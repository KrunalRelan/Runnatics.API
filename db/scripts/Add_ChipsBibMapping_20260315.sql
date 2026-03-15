-- =============================================
-- Script: Add_ChipsBibMapping_20260315.sql
-- Purpose: Ensure Chips and ChipAssignments tables exist
--          with all required columns for BIB-EPC mapping feature
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written SQL only
-- =============================================

BEGIN TRANSACTION;

-- ── Chips Table ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Chips')
BEGIN
    CREATE TABLE [dbo].[Chips]
    (
        [Id]            INT             IDENTITY(1,1)   NOT NULL,
        [TenantId]      INT             NOT NULL,
        [EPC]           NVARCHAR(50)    NOT NULL,
        [Status]        NVARCHAR(20)    NOT NULL        DEFAULT 'Available',
        [BatteryLevel]  INT             NULL,
        [LastSeenAt]    DATETIME2       NULL,
        [Notes]         NVARCHAR(MAX)   NULL,

        -- AuditProperties columns
        [CreatedAt]     DATETIME2       NOT NULL        DEFAULT GETUTCDATE(),
        [UpdatedAt]     DATETIME2       NULL,
        [CreatedBy]     INT             NULL,
        [UpdatedBy]     INT             NULL,
        [IsActive]      BIT             NOT NULL        DEFAULT 1,
        [IsDeleted]     BIT             NOT NULL        DEFAULT 0,

        CONSTRAINT [PK_Chips] PRIMARY KEY CLUSTERED ([Id])
    );

    -- Unique EPC index
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Chips_EPC]
        ON [dbo].[Chips] ([EPC]);

    -- Tenant + Status for filtered queries
    CREATE NONCLUSTERED INDEX [IX_Chips_TenantId_Status]
        ON [dbo].[Chips] ([TenantId], [Status]);

    PRINT 'Created table: Chips';
END
ELSE
BEGIN
    PRINT 'Table already exists: Chips';
END
GO

-- ── ChipAssignments Table ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChipAssignments')
BEGIN
    CREATE TABLE [dbo].[ChipAssignments]
    (
        [EventId]           INT         NOT NULL,
        [ParticipantId]     INT         NOT NULL,
        [ChipId]            INT         NOT NULL,
        [AssignedAt]        DATETIME2   NOT NULL    DEFAULT GETUTCDATE(),
        [UnassignedAt]      DATETIME2   NULL,
        [AssignedByUserId]  INT         NULL,

        -- AuditProperties columns
        [CreatedAt]         DATETIME2   NOT NULL    DEFAULT GETUTCDATE(),
        [UpdatedAt]         DATETIME2   NULL,
        [CreatedBy]         INT         NULL,
        [UpdatedBy]         INT         NULL,
        [IsActive]          BIT         NOT NULL    DEFAULT 1,
        [IsDeleted]         BIT         NOT NULL    DEFAULT 0,

        CONSTRAINT [PK_ChipAssignments] PRIMARY KEY CLUSTERED ([EventId], [ParticipantId], [ChipId]),

        CONSTRAINT [FK_ChipAssignments_Events] FOREIGN KEY ([EventId])
            REFERENCES [dbo].[Events] ([Id]) ON DELETE CASCADE,

        CONSTRAINT [FK_ChipAssignments_Participants] FOREIGN KEY ([ParticipantId])
            REFERENCES [dbo].[Participants] ([Id]) ON DELETE NO ACTION,

        CONSTRAINT [FK_ChipAssignments_Chips] FOREIGN KEY ([ChipId])
            REFERENCES [dbo].[Chips] ([Id]) ON DELETE NO ACTION,

        CONSTRAINT [FK_ChipAssignments_Users] FOREIGN KEY ([AssignedByUserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE SET NULL
    );

    -- Individual FK indexes for join performance
    CREATE NONCLUSTERED INDEX [IX_ChipAssignments_EventId]
        ON [dbo].[ChipAssignments] ([EventId]);

    CREATE NONCLUSTERED INDEX [IX_ChipAssignments_ParticipantId]
        ON [dbo].[ChipAssignments] ([ParticipantId]);

    CREATE NONCLUSTERED INDEX [IX_ChipAssignments_ChipId]
        ON [dbo].[ChipAssignments] ([ChipId]);

    CREATE NONCLUSTERED INDEX [IX_ChipAssignments_AssignedAt]
        ON [dbo].[ChipAssignments] ([AssignedAt]);

    PRINT 'Created table: ChipAssignments';
END
ELSE
BEGIN
    PRINT 'Table already exists: ChipAssignments';
END

COMMIT;
GO
