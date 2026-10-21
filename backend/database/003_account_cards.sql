-- Ameen ERP schema 003: GL01 account labels and explicit normal balance.
-- Apply after 001 and 002; prior migrations remain immutable.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 2)
        THROW 51001, 'Apply schema 002 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 3)
    BEGIN
        EXEC(N'ALTER TABLE [Accounts] ADD [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL, [NormalBalance] int NULL, [Revision] int NULL;');
        -- Legacy cards lacked an explicit nature: infer a starting value from classification.
        -- Keep IDs, versions, hierarchy and all financial data unchanged.
        EXEC(N'UPDATE [Accounts] SET [NormalBalance] = CASE WHEN [Type] IN (0,4) THEN 0 ELSE 1 END, [Revision] = 1;');
        EXEC(N'ALTER TABLE [Accounts] ALTER COLUMN [NormalBalance] int NOT NULL;');
        EXEC(N'ALTER TABLE [Accounts] ALTER COLUMN [Revision] int NOT NULL;');
        EXEC(N'ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Account_NormalBalance] CHECK ([NormalBalance] BETWEEN 0 AND 1);');
        EXEC(N'ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Account_Revision] CHECK ([Revision] > 0);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (3, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
