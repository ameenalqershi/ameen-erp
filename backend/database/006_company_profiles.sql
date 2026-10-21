-- SYS01 profile and company lifecycle. Legacy missing legal/address data remains NULL.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 5)
        THROW 51001, 'Apply schema 005 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 6)
    BEGIN
        EXEC(N'ALTER TABLE [Companies] ADD [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [LegalName] nvarchar(200) NULL, [Jurisdiction] nvarchar(200) NULL, [FiscalYearStartsOn] date NULL,
            [CommercialRegistration] nvarchar(64) NULL, [TaxRegistration] nvarchar(64) NULL,
            [City] nvarchar(200) NULL, [District] nvarchar(200) NULL, [Street] nvarchar(300) NULL,
            [BuildingNumber] nvarchar(32) NULL, [PostalCode] nvarchar(32) NULL, [AdditionalNumber] nvarchar(32) NULL,
            [Revision] int NULL, [IsActive] bit NULL;');
        EXEC(N'UPDATE c SET [Revision]=1, [IsActive]=1,
            [FiscalYearStartsOn]=(SELECT MIN(y.[StartsOn]) FROM [FiscalYears] y WHERE y.[CompanyId]=c.[Id]) FROM [Companies] c;');
        EXEC(N'ALTER TABLE [Companies] ALTER COLUMN [Revision] int NOT NULL;');
        EXEC(N'ALTER TABLE [Companies] ALTER COLUMN [IsActive] bit NOT NULL;');
        EXEC(N'ALTER TABLE [Companies] ADD CONSTRAINT [CK_Company_Revision] CHECK ([Revision] > 0);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (6, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
