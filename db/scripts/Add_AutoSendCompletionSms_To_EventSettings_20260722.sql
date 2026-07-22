-- =============================================
-- Script: Add_AutoSendCompletionSms_To_EventSettings_20260722.sql
-- Purpose: Per-event toggle gating the auto-send of completion SMS (default OFF).
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written idempotent SQL only.
-- ORDERING: run this BEFORE the API that maps EventSettings.AutoSendCompletionSms
--           deploys, or EventSettings reads will fail until the column exists.
-- =============================================

IF COL_LENGTH('dbo.EventSettings', 'AutoSendCompletionSms') IS NULL
BEGIN
    ALTER TABLE [dbo].[EventSettings]
        ADD [AutoSendCompletionSms] BIT NOT NULL
            CONSTRAINT [DF_EventSettings_AutoSendCompletionSms] DEFAULT (0);
    PRINT 'Added column: EventSettings.AutoSendCompletionSms';
END
ELSE
BEGIN
    PRINT 'Column already exists: EventSettings.AutoSendCompletionSms';
END
GO
