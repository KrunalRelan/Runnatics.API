-- =============================================
-- Script: Centre_ParticipantName_On_CertificateLine_20260828.sql
-- Purpose: Centre the participant name on the dotted line of the Twin Lake Ultra
--          finisher certificates (event 66, races 100/101/102 = templates 8/7/6).
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written idempotent SQL only.
--
-- ORDERING: run this AFTER the API carrying the CertificatesService box-alignment
--           change deploys. The renderer only honours CertificateFields.Width once
--           that build is live; against the old build these rows still render
--           left-anchored at XCoordinate, which for X=125 would sit further left
--           than today rather than centred.
--
-- BACKGROUND: CertificatesService.RenderToPng already resolved Alignment to an
-- SKTextAlign, but every field row in every template was created by the editor with
-- Alignment='left' and Width=NULL (the editor had no alignment control), so the name
-- was drawn left-anchored from a hand-dragged X and drifted off the line's centre —
-- 108 px left for a short name, 112 px right for a long one, and clean off the canvas
-- for a very long one.
--
-- GEOMETRY: the dotted rule was measured directly from each template's background
-- bitmap. Templates 6, 7 and 8 share identical artwork geometry:
--     dotted line x = 125 .. 959   (width 834, centre 542)
--     template width 1086          (page centre 543)
-- Setting the field to X=125 / Width=834 / Alignment='center' anchors the name on
-- 542 and confines it to the rule; the renderer auto-shrinks (to 60% of the font
-- size) and only then ellipsis-truncates, so long names stay whole and inside.
--
-- NOT INCLUDED — deliberately:
--   Template 1 (St Lawrence Marathon) has no ParticipantName field at all.
--   Template 2 (Dharamshala Marathon) has no dotted rule; the name sits in an open band.
--   Template 4 (26th APR Event) points at the Dharamshala artwork at mismatched
--             dimensions (declared 1400x1500 vs a 1080x1920 bitmap) — already broken.
--   Template 5 (copy of 26th APR event) uses an event poster as its background.
--   All four belong to past events. The renderer change is universal and harmless to
--   them; only this coordinate data is template-specific.
-- =============================================

SET NOCOUNT ON;
GO

-- FieldType 0 = CertificateFieldType.ParticipantName
DECLARE @ParticipantName INT = 0;

DECLARE @LineLeft  INT = 125;
DECLARE @LineWidth INT = 834;

-- Show the rows about to change
SELECT 'BEFORE' AS Phase, f.Id, f.TemplateId, f.XCoordinate, f.Width, f.Alignment
FROM   dbo.CertificateFields f
WHERE  f.TemplateId IN (6, 7, 8)
  AND  f.FieldType  = @ParticipantName
  AND  f.IsActive   = 1
  AND  f.IsDeleted  = 0;

UPDATE f
SET    f.XCoordinate = @LineLeft,
       f.Width       = @LineWidth,
       f.Alignment   = 'center',
       f.UpdatedAt   = SYSUTCDATETIME()
FROM   dbo.CertificateFields f
WHERE  f.TemplateId IN (6, 7, 8)
  AND  f.FieldType  = @ParticipantName
  AND  f.IsActive   = 1
  AND  f.IsDeleted  = 0
  AND  (f.XCoordinate <> @LineLeft            -- idempotent: re-running changes nothing
     OR f.Width IS NULL OR f.Width <> @LineWidth
     OR f.Alignment IS NULL OR f.Alignment <> 'center');

PRINT CONCAT('Rows updated: ', @@ROWCOUNT);
GO

-- Verify
SELECT 'AFTER' AS Phase, f.Id, f.TemplateId, f.XCoordinate, f.Width, f.Alignment,
       f.XCoordinate + f.Width / 2 AS ResolvedCentre   -- expect 542 on all three rows
FROM   dbo.CertificateFields f
WHERE  f.TemplateId IN (6, 7, 8)
  AND  f.FieldType  = 0
  AND  f.IsActive   = 1
  AND  f.IsDeleted  = 0;
GO

-- Rollback, if the centring ever needs reverting to the pre-change coordinates:
--   UPDATE dbo.CertificateFields
--   SET    XCoordinate = 370, Width = NULL, Alignment = 'left'
--   WHERE  TemplateId IN (6, 7, 8) AND FieldType = 0 AND IsActive = 1 AND IsDeleted = 0;
