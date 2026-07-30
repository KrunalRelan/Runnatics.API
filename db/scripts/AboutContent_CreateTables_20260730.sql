-- =============================================
-- Script: AboutContent_CreateTables_20260730.sql
-- Purpose: Editable public About page (mini-CMS) — SiteContents key-value copy
--          store + Founders tiles. Seeds SiteContents with the copy that is
--          hardcoded on the live site today, so the page loses nothing on deploy.
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written SQL only.
--             No cross-batch transaction (each batch is idempotent on its own).
-- =============================================

-- ── SiteContents (key-value page copy) ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SiteContents')
BEGIN
    CREATE TABLE [dbo].[SiteContents]
    (
        [Id]            INT             IDENTITY(1,1)       NOT NULL,
        [ContentKey]    NVARCHAR(100)                       NOT NULL,
        [ContentValue]  NVARCHAR(MAX)                       NULL,

        [CreatedBy]     INT                                 NULL,
        [CreatedAt]     DATETIME2       DEFAULT GETUTCDATE() NOT NULL,
        [UpdatedBy]     INT                                 NULL,
        [UpdatedAt]     DATETIME2                           NULL,
        [IsDeleted]     BIT             DEFAULT 0           NOT NULL,
        [IsActive]      BIT             DEFAULT 1           NOT NULL,

        CONSTRAINT [PK_SiteContents]            PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [UQ_SiteContents_ContentKey] UNIQUE ([ContentKey])
    );

    PRINT 'Created table: SiteContents';
END
ELSE
BEGIN
    PRINT 'Table already exists: SiteContents';
END
GO

-- ── Founders (About page tiles) ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Founders')
BEGIN
    CREATE TABLE [dbo].[Founders]
    (
        [Id]            INT             IDENTITY(1,1)       NOT NULL,
        [Name]          NVARCHAR(200)                       NOT NULL,
        [Role]          NVARCHAR(200)                       NULL,
        [Bio]           NVARCHAR(1000)                      NULL,
        [PhotoBase64]   NVARCHAR(MAX)                       NULL,
        [DisplayOrder]  INT             DEFAULT 0           NOT NULL,

        [CreatedBy]     INT                                 NULL,
        [CreatedAt]     DATETIME2       DEFAULT GETUTCDATE() NOT NULL,
        [UpdatedBy]     INT                                 NULL,
        [UpdatedAt]     DATETIME2                           NULL,
        [IsDeleted]     BIT             DEFAULT 0           NOT NULL,
        [IsActive]      BIT             DEFAULT 1           NOT NULL,

        CONSTRAINT [PK_Founders] PRIMARY KEY CLUSTERED ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_Founders_DisplayOrder]
        ON [dbo].[Founders] ([DisplayOrder]);

    PRINT 'Created table: Founders';
END
ELSE
BEGIN
    PRINT 'Table already exists: Founders';
END
GO

-- ── Seed About copy with the text hardcoded on the live site today ──
-- Idempotent per key: inserts only when the key is absent, never overwrites an
-- admin's later edits.
IF NOT EXISTS (SELECT * FROM [dbo].[SiteContents] WHERE [ContentKey] = 'About.WhoWeAre')
BEGIN
    INSERT INTO [dbo].[SiteContents] ([ContentKey], [ContentValue])
    VALUES ('About.WhoWeAre',
N'Racetik Timing Solution is committed to delivering precise, reliable, and end-to-end event timing and management services. With a focus on innovation, scalability, and client collaboration, we ensure seamless execution and a superior experience for every event—making us a trusted partner for organizers seeking accuracy, efficiency, and excellence.');
    PRINT 'Seeded: About.WhoWeAre';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SiteContents] WHERE [ContentKey] = 'About.Mission')
BEGIN
    INSERT INTO [dbo].[SiteContents] ([ContentKey], [ContentValue])
    VALUES ('About.Mission',
N'Our mission is to provide every organizer and athlete with an uncompromising record of performance. By combining deep industry expertise with collaborative partnerships, we empower event organizers to deliver seamless, world-class experiences. We believe in shared success: as the events we support reach new heights, we grow alongside them.');
    PRINT 'Seeded: About.Mission';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SiteContents] WHERE [ContentKey] = 'About.StoryImage')
BEGIN
    INSERT INTO [dbo].[SiteContents] ([ContentKey], [ContentValue])
    VALUES ('About.StoryImage', NULL);
    PRINT 'Seeded: About.StoryImage (empty)';
END
GO

PRINT 'AboutContent_CreateTables_20260730.sql complete.';
GO
