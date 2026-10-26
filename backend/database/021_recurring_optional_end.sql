-- Ameen ERP schema 021: GL04-F07 optional end date for recurring templates.
-- This migration never rewrites historical templates or generated accounting journals.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=20) THROW 51001, 'Apply schema 020 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=21)
    BEGIN
        ALTER TABLE [RecurringJournalTemplates] DROP CONSTRAINT [CK_Recurring_Dates];
        ALTER TABLE [RecurringJournalTemplates] ALTER COLUMN [EndsOn] date NULL;
        ALTER TABLE [RecurringJournalTemplates] ADD CONSTRAINT [CK_Recurring_Dates]
            CHECK ([EndsOn] IS NULL OR [EndsOn] >= [StartsOn]);
        INSERT INTO dbo.__AmeenSchemaVersions ([Version],[AppliedAt]) VALUES (21,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
