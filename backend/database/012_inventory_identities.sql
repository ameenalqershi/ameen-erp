SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 11) THROW 51001, 'Apply schema 011 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 12)
    BEGIN
        EXEC(N'CREATE TABLE [InventoryIdentities] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [ItemId] uniqueidentifier NOT NULL, [Tracking] int NOT NULL,
            [Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
            [Name] nvarchar(200) NOT NULL, [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [ProductionDate] date NULL, [ExpiryDate] date NULL, [QualityStatus] int NOT NULL,
            [IsActive] bit NOT NULL, [Revision] int NOT NULL,
            CONSTRAINT [PK_InventoryIdentities] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_InventoryIdentities_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [FK_InventoryIdentities_Items_CompanyId_ItemId] FOREIGN KEY ([CompanyId], [ItemId]) REFERENCES [Items] ([CompanyId], [Id]),
            CONSTRAINT [CK_InventoryIdentity_Revision] CHECK ([Revision] > 0),
            CONSTRAINT [CK_InventoryIdentity_Tracking] CHECK ([Tracking] IN (1, 2)),
            CONSTRAINT [CK_InventoryIdentity_Quality] CHECK ([QualityStatus] BETWEEN 0 AND 2),
            CONSTRAINT [CK_InventoryIdentity_Expiry] CHECK ([ProductionDate] IS NULL OR [ExpiryDate] IS NULL OR [ExpiryDate] > [ProductionDate])
        );');
        EXEC(N'CREATE UNIQUE INDEX [IX_InventoryIdentities_CompanyId_ItemId_Tracking_Code] ON [InventoryIdentities] ([CompanyId], [ItemId], [Tracking], [Code]);');
        EXEC(N'CREATE INDEX [IX_InventoryIdentities_CompanyId_ItemId_QualityStatus] ON [InventoryIdentities] ([CompanyId], [ItemId], [QualityStatus]);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (12, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
