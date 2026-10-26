-- Ameen ERP schema 019: explicit scheduled draft generation mode.
-- Retains existing templates, lines, accounting journals, and all source analysis unchanged.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=18) THROW 51001, 'Apply schema 018 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=19)
    BEGIN
        ALTER TABLE [RecurringJournalTemplates] DROP CONSTRAINT [CK_Recurring_Mode];
        ALTER TABLE [RecurringJournalTemplates] ADD CONSTRAINT [CK_Recurring_Mode]
            CHECK ([GenerationMode] IN (0, 1));
        INSERT INTO dbo.__AmeenSchemaVersions([Version], AppliedAt) VALUES(19,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
