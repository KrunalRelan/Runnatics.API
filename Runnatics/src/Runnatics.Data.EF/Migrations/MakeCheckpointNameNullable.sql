-- Make Checkpoint Name column nullable
-- This allows checkpoints to not have a name when a Parent Checkpoint is selected

ALTER TABLE [dbo].[Checkpoints]
ALTER COLUMN [Name] NVARCHAR(100) NULL;
