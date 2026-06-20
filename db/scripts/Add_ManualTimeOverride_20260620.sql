-- =============================================
-- Script: Add_ManualTimeOverride_20260620.sql
-- Purpose: Durable manual-time override store. The authoritative INPUT layer for
--          manual time corrections — survives ClearProcessedData / reprocess / race move,
--          and is re-applied onto ReadNormalized by Phase 2.4 on every rebuild.
--          (raw RawRFIDReading = hardware truth; ManualTimeOverride = durable override;
--           ReadNormalized/SplitTimes/Results = derived, rebuilt every reprocess.)
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written idempotent SQL only. Run manually.
-- =============================================

BEGIN TRANSACTION;

-- ── ManualTimeOverrides ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ManualTimeOverrides')
BEGIN
    CREATE TABLE [dbo].[ManualTimeOverrides]
    (
        [Id]                INT             IDENTITY(1,1)   NOT NULL,
        [EventId]           INT             NOT NULL,
        [RaceId]            INT             NOT NULL,
        [ParticipantId]     INT             NOT NULL,
        [CheckpointId]      INT             NOT NULL,
        [ManualCrossingUtc] DATETIME2       NOT NULL,
        [Reason]            NVARCHAR(500)   NULL,
        [CreatedByUserId]   INT             NULL,

        -- AuditProperties (owned)
        [IsActive]          BIT             NOT NULL        DEFAULT 1,
        [IsDeleted]         BIT             NOT NULL        DEFAULT 0,
        [CreatedBy]         INT             NOT NULL,
        [CreatedDate]       DATETIME2       NOT NULL        DEFAULT GETUTCDATE(),
        [UpdatedBy]         INT             NULL,
        [UpdatedDate]       DATETIME2       NULL,

        CONSTRAINT [PK_ManualTimeOverrides] PRIMARY KEY CLUSTERED ([Id]),

        CONSTRAINT [FK_ManualTimeOverrides_Event] FOREIGN KEY ([EventId])
            REFERENCES [dbo].[Events] ([Id]),

        CONSTRAINT [FK_ManualTimeOverrides_Race] FOREIGN KEY ([RaceId])
            REFERENCES [dbo].[Races] ([Id]),

        CONSTRAINT [FK_ManualTimeOverrides_Participant] FOREIGN KEY ([ParticipantId])
            REFERENCES [dbo].[Participants] ([Id]),

        CONSTRAINT [FK_ManualTimeOverrides_Checkpoint] FOREIGN KEY ([CheckpointId])
            REFERENCES [dbo].[Checkpoints] ([Id]),

        CONSTRAINT [FK_ManualTimeOverrides_CreatedByUser] FOREIGN KEY ([CreatedByUserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE SET NULL
    );

    -- One ACTIVE override per (participant, checkpoint). Filtered so soft-deleted rows
    -- fall OUT of the index — a revert (IsDeleted=1) releases the slot, letting a later
    -- re-override of the same checkpoint insert cleanly.
    CREATE UNIQUE INDEX [UX_ManualTimeOverride_Participant_Checkpoint_Active]
        ON [dbo].[ManualTimeOverrides] ([ParticipantId], [CheckpointId])
        WHERE [IsDeleted] = 0;

    -- Phase 2.4 scans active overrides by race.
    CREATE NONCLUSTERED INDEX [IX_ManualTimeOverrides_Event_Race]
        ON [dbo].[ManualTimeOverrides] ([EventId], [RaceId])
        WHERE [IsDeleted] = 0;

    PRINT 'Created table: ManualTimeOverrides';
END
ELSE
BEGIN
    PRINT 'Table already exists: ManualTimeOverrides';
END

COMMIT;
GO
