-- SYS04 currency lifecycle and descriptive fields. Apply after schema 003.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 3)
        THROW 51001, 'Apply schema 003 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 4)
    BEGIN
        EXEC(N'ALTER TABLE [Currencies] ADD [Name] nvarchar(200) NULL, [EnglishName] nvarchar(200) NULL,
            [Notes] nvarchar(2000) NULL, [DisplaySymbol] nvarchar(20) NULL, [FractionName] nvarchar(100) NULL,
            [Revision] int NULL, [IsActive] bit NULL;');
        -- No language label is invented for old cards; ISO code is a visible placeholder.
        EXEC(N'UPDATE [Currencies] SET [Name]=[Code], [Revision]=1, [IsActive]=1;');
        EXEC(N'ALTER TABLE [Currencies] ALTER COLUMN [Name] nvarchar(200) NOT NULL;');
        EXEC(N'ALTER TABLE [Currencies] ALTER COLUMN [Revision] int NOT NULL;');
        EXEC(N'ALTER TABLE [Currencies] ALTER COLUMN [IsActive] bit NOT NULL;');
        EXEC(N'ALTER TABLE [Currencies] ADD CONSTRAINT [CK_Currency_Revision] CHECK ([Revision] > 0);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (4, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
