-- =============================================
-- Script: SupportQuery_AddTenantId_20260726.sql
-- Purpose: Tenant-scope support tickets. Adds SupportQueries.TenantId (NULLABLE) and
--          backfills it by inference. Fixes: any authenticated user of ANY tenant could
--          previously read/edit/delete every support ticket in the system.
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written, idempotent SQL only.
--
-- !! DEPLOY ORDER !!  RUN THIS SCRIPT **BEFORE** deploying the API build that maps
--    SupportQuery.TenantId. EF selects every mapped column, so the API will throw
--    "Invalid column name 'TenantId'" on EVERY support read until this has run.
--
-- Backfill policy (agreed 2026-07-26): infer where possible, NULL otherwise.
--   1. Assigned tickets      -> the assignee's TenantId
--   2. Otherwise, if SubmitterEmail matches exactly ONE User -> that user's TenantId
--   3. Everything else       -> NULL = platform-level pool, visible to SuperAdmin only
-- Nothing is ever guessed into a tenant it might not belong to.
-- =============================================

-- Deliberately NO script-wide transaction: GO is a client-side batch separator, so a
-- BEGIN TRAN here would stay open across batches and — because SSMS continues after a
-- failed batch — could leave an open transaction holding a schema lock on SupportQueries.
-- Every step below is independently idempotent instead, so a partial run is safe and
-- simply re-running the script completes it.
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── (1) Column ────────────────────────────────────────────────────────────────
-- NULLABLE by design: POST /api/support/contact is [AllowAnonymous], so a public
-- submitter has no JWT and therefore no tenant. NULL = platform pool.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[SupportQueries]') AND name = 'TenantId'
)
BEGIN
    ALTER TABLE [dbo].[SupportQueries] ADD [TenantId] INT NULL;
    PRINT 'Added column: SupportQueries.TenantId';
END
ELSE
BEGIN
    PRINT 'Column already exists: SupportQueries.TenantId';
END
GO

-- ── (2) Index ─────────────────────────────────────────────────────────────────
-- Every admin list/count query now filters on TenantId; StatusId is the usual
-- companion predicate (status tabs), so include it in the key.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[SupportQueries]')
      AND name = 'IX_SupportQueries_TenantId_StatusId'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SupportQueries_TenantId_StatusId]
        ON [dbo].[SupportQueries] ([TenantId], [StatusId]);
    PRINT 'Created index: IX_SupportQueries_TenantId_StatusId';
END
ELSE
BEGIN
    PRINT 'Index already exists: IX_SupportQueries_TenantId_StatusId';
END
GO

-- ── (3) Backfill step 1 — infer from the assignee ─────────────────────────────
-- Only touches rows still NULL, so re-running this script is safe and never
-- overwrites a tenant an admin has since corrected by hand.
UPDATE sq
   SET sq.TenantId = u.TenantId
  FROM [dbo].[SupportQueries] sq
  INNER JOIN [dbo].[Users] u ON u.Id = sq.AssignedToUserId
 WHERE sq.TenantId IS NULL
   AND sq.AssignedToUserId IS NOT NULL;

PRINT CONCAT('Backfill step 1 (from assignee): ', @@ROWCOUNT, ' row(s).');
GO

-- ── (4) Backfill step 2 — infer from the submitter's email ────────────────────
-- Guarded to emails that resolve to EXACTLY ONE user. If the same address exists
-- under two tenants we cannot tell which one owns the ticket, so it stays NULL
-- rather than leaking into the wrong tenant.
UPDATE sq
   SET sq.TenantId = m.TenantId
  FROM [dbo].[SupportQueries] sq
  INNER JOIN (
        SELECT u.Email, MIN(u.TenantId) AS TenantId
          FROM [dbo].[Users] u
         GROUP BY u.Email
        HAVING COUNT(DISTINCT u.TenantId) = 1
  ) m ON m.Email = sq.SubmitterEmail
 WHERE sq.TenantId IS NULL;

PRINT CONCAT('Backfill step 2 (from submitter email): ', @@ROWCOUNT, ' row(s).');
GO

-- ── (5) Verification — review before trusting the result ──────────────────────
SELECT
    COUNT(*)                                                AS TotalTickets,
    SUM(CASE WHEN TenantId IS NOT NULL THEN 1 ELSE 0 END)   AS TenantAssigned,
    SUM(CASE WHEN TenantId IS NULL     THEN 1 ELSE 0 END)   AS PlatformPool_SuperAdminOnly
FROM [dbo].[SupportQueries];

SELECT TenantId, COUNT(*) AS Tickets
FROM [dbo].[SupportQueries]
GROUP BY TenantId
ORDER BY CASE WHEN TenantId IS NULL THEN 1 ELSE 0 END, TenantId;
GO
