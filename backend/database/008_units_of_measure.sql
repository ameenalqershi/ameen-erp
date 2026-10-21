-- INV03 general units only; item conversions are not global unit attributes.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 7)
        THROW 51001, 'Apply schema 007 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 8)
    BEGIN
        CREATE TABLE [UnitsOfMeasure] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL, [StandardCode] nvarchar(32) NOT NULL,
            [QuantityScale] int NOT NULL, [Description] nvarchar(500) NOT NULL,
            [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL, [IsActive] bit NOT NULL, [Revision] int NOT NULL,
            CONSTRAINT [PK_UnitsOfMeasure] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_UnitsOfMeasure_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [CK_Unit_QuantityScale] CHECK ([QuantityScale] BETWEEN 0 AND 6),
            CONSTRAINT [CK_Unit_Revision] CHECK ([Revision] > 0)
        );
        CREATE UNIQUE INDEX [IX_UnitsOfMeasure_CompanyId_Code] ON [UnitsOfMeasure] ([CompanyId], [Code]);
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (8, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
