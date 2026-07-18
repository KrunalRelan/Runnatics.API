-- Add Thumbnail + ThumbnailContentType to Events.
-- Every event gets TWO images: Banner (large hero) and Thumbnail (event tiles).
-- Thumbnail is optional; when absent the API falls back to the banner for tiles.
-- Base64-encoded image stored in DB, mirroring BannerImage/BannerContentType.
-- Idempotent: safe to re-run.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'Thumbnail'
      AND Object_ID = Object_ID(N'dbo.Events')
)
BEGIN
    ALTER TABLE dbo.Events ADD Thumbnail NVARCHAR(MAX) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'ThumbnailContentType'
      AND Object_ID = Object_ID(N'dbo.Events')
)
BEGIN
    ALTER TABLE dbo.Events ADD ThumbnailContentType NVARCHAR(50) NULL;
END
GO
