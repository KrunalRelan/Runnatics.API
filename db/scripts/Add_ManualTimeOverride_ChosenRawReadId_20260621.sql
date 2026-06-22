-- =============================================
-- Script: Add_ManualTimeOverride_ChosenRawReadId_20260621.sql
-- Purpose: Hybrid "choose which raw read is the crossing" support. Adds a nullable
--          ChosenRawReadId to ManualTimeOverrides so an override can record WHICH raw
--          read the operator selected (not just its time):
--            - ChosenRawReadId NULL  → typed manual time (no underlying raw read), exact
--                                      legacy behaviour. RawReadId on ReadNormalized = NULL,
--                                      IsManualEntry = 1.
--            - ChosenRawReadId set    → operator picked a real hardware read at this checkpoint.
--                                      Phase 2.4 sets ReadNormalized.RawReadId = ChosenRawReadId
--                                      and IsManualEntry = 0 (real read, manually SELECTED) so the
--                                      chosen read highlights as normalized.
--          ManualCrossingUtc still stores the chosen read's time, so timing stays correct even
--          if the raw read is later hard-deleted (keepUploads=false clear) — apply then degrades
--          to RawReadId=NULL using ManualCrossingUtc.
-- NOTE:   Deliberately NO FK on ChosenRawReadId. Raw reads can be hard-deleted on a
--          clear-with-keepUploads=false; the override MUST survive that (durability). A plain
--          nullable scalar; the apply path resolves it best-effort.
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written idempotent SQL only. Run manually.
-- =============================================

BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[ManualTimeOverrides]')
      AND name = 'ChosenRawReadId'
)
BEGIN
    ALTER TABLE [dbo].[ManualTimeOverrides]
        ADD [ChosenRawReadId] BIGINT NULL;

    PRINT 'Added column: ManualTimeOverrides.ChosenRawReadId';
END
ELSE
BEGIN
    PRINT 'Column already exists: ManualTimeOverrides.ChosenRawReadId';
END

COMMIT;
GO
