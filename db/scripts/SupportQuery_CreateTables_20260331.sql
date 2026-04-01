-- =============================================
-- Script: SupportQuery_CreateTables_20260331.sql
-- Purpose: Create support query tables for the Contact Us feature
-- Target: Azure SQL Database
-- Convention: NO EF Migrations — hand-written SQL only
-- =============================================

BEGIN TRANSACTION;

-- ── SupportQueryStatus (lookup) ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SupportQueryStatuses')
BEGIN
    CREATE TABLE [dbo].[SupportQueryStatuses]
    (
        [Id]    INT             IDENTITY(1,1)   NOT NULL,
        [Name]  NVARCHAR(50)    NOT NULL,

        CONSTRAINT [PK_SupportQueryStatuses]    PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [UQ_SupportQueryStatuses_Name] UNIQUE ([Name])
    );

    -- Seed statuses
    SET IDENTITY_INSERT [dbo].[SupportQueryStatuses] ON;
    INSERT INTO [dbo].[SupportQueryStatuses] ([Id], [Name]) VALUES
        (1, 'new_query'),
        (2, 'wip'),
        (3, 'closed'),
        (4, 'pending'),
        (5, 'not_yet_started'),
        (6, 'rejected'),
        (7, 'duplicate');
    SET IDENTITY_INSERT [dbo].[SupportQueryStatuses] OFF;

    PRINT 'Created table: SupportQueryStatuses (with seed data)';
END
ELSE
BEGIN
    PRINT 'Table already exists: SupportQueryStatuses';
END
GO

-- ── SupportQueryType (lookup — admin configured) ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SupportQueryTypes')
BEGIN
    CREATE TABLE [dbo].[SupportQueryTypes]
    (
        [Id]    INT             IDENTITY(1,1)   NOT NULL,
        [Name]  NVARCHAR(100)   NOT NULL,

        CONSTRAINT [PK_SupportQueryTypes]     PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [UQ_SupportQueryTypes_Name] UNIQUE ([Name])
    );

    PRINT 'Created table: SupportQueryTypes';
END
ELSE
BEGIN
    PRINT 'Table already exists: SupportQueryTypes';
END
GO

-- ── SupportQuery ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SupportQueries')
BEGIN
    CREATE TABLE [dbo].[SupportQueries]
    (
        [Id]                INT             IDENTITY(1,1)   NOT NULL,
        [Subject]           NVARCHAR(255)   NOT NULL,
        [Body]              NVARCHAR(MAX)   NOT NULL,
        [SubmitterEmail]    NVARCHAR(255)   NOT NULL,
        [StatusId]          INT             NOT NULL        DEFAULT 1,
        [QueryTypeId]       INT             NULL,
        [AssignedToUserId]  INT             NULL,
        [CreatedAt]         DATETIME2       NOT NULL        DEFAULT GETUTCDATE(),
        [UpdatedAt]         DATETIME2       NOT NULL        DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_SupportQueries] PRIMARY KEY CLUSTERED ([Id]),

        CONSTRAINT [FK_SupportQueries_Status] FOREIGN KEY ([StatusId])
            REFERENCES [dbo].[SupportQueryStatuses] ([Id]),

        CONSTRAINT [FK_SupportQueries_QueryType] FOREIGN KEY ([QueryTypeId])
            REFERENCES [dbo].[SupportQueryTypes] ([Id]),

        CONSTRAINT [FK_SupportQueries_AssignedUser] FOREIGN KEY ([AssignedToUserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE SET NULL
    );

    CREATE NONCLUSTERED INDEX [IX_SupportQueries_StatusId]
        ON [dbo].[SupportQueries] ([StatusId]);

    CREATE NONCLUSTERED INDEX [IX_SupportQueries_AssignedToUserId]
        ON [dbo].[SupportQueries] ([AssignedToUserId]);

    CREATE NONCLUSTERED INDEX [IX_SupportQueries_SubmitterEmail]
        ON [dbo].[SupportQueries] ([SubmitterEmail]);

    CREATE NONCLUSTERED INDEX [IX_SupportQueries_CreatedAt]
        ON [dbo].[SupportQueries] ([CreatedAt] DESC);

    PRINT 'Created table: SupportQueries';
END
ELSE
BEGIN
    PRINT 'Table already exists: SupportQueries';
END
GO

-- ── SupportQueryComment ──
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SupportQueryComments')
BEGIN
    CREATE TABLE [dbo].[SupportQueryComments]
    (
        [Id]                INT             IDENTITY(1,1)   NOT NULL,
        [SupportQueryId]    INT             NOT NULL,
        [CommentText]       NVARCHAR(MAX)   NOT NULL,
        [TicketStatusId]    INT             NOT NULL,
        [NotificationSent]  BIT             NOT NULL        DEFAULT 0,
        [CreatedAt]         DATETIME2       NOT NULL        DEFAULT GETUTCDATE(),
        [CreatedByUserId]   INT             NULL,

        CONSTRAINT [PK_SupportQueryComments] PRIMARY KEY CLUSTERED ([Id]),

        CONSTRAINT [FK_SupportQueryComments_Query] FOREIGN KEY ([SupportQueryId])
            REFERENCES [dbo].[SupportQueries] ([Id]) ON DELETE CASCADE,

        CONSTRAINT [FK_SupportQueryComments_Status] FOREIGN KEY ([TicketStatusId])
            REFERENCES [dbo].[SupportQueryStatuses] ([Id]),

        CONSTRAINT [FK_SupportQueryComments_User] FOREIGN KEY ([CreatedByUserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE SET NULL
    );

    CREATE NONCLUSTERED INDEX [IX_SupportQueryComments_SupportQueryId]
        ON [dbo].[SupportQueryComments] ([SupportQueryId]);

    CREATE NONCLUSTERED INDEX [IX_SupportQueryComments_CreatedAt]
        ON [dbo].[SupportQueryComments] ([CreatedAt] DESC);

    PRINT 'Created table: SupportQueryComments';
END
ELSE
BEGIN
    PRINT 'Table already exists: SupportQueryComments';
END

COMMIT;
GO
