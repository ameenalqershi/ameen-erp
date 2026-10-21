SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 9) THROW 51001, 'Apply schema 009 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 10)
    BEGIN
        EXEC(N'CREATE TABLE [Items] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL, [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [Type] int NOT NULL, [Tracking] int NOT NULL, [GroupId] uniqueidentifier NOT NULL, [BaseUnitId] uniqueidentifier NOT NULL,
            [ShelfLifeDays] int NULL, [ReorderLevel] decimal(20,6) NULL, [IsActive] bit NOT NULL, [Revision] int NOT NULL,
            CONSTRAINT [PK_Items] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_Items_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [FK_Items_ItemGroups_CompanyId_GroupId] FOREIGN KEY ([CompanyId], [GroupId]) REFERENCES [ItemGroups] ([CompanyId], [Id]),
            CONSTRAINT [FK_Items_UnitsOfMeasure_CompanyId_BaseUnitId] FOREIGN KEY ([CompanyId], [BaseUnitId]) REFERENCES [UnitsOfMeasure] ([CompanyId], [Id]),
            CONSTRAINT [CK_Item_Revision] CHECK ([Revision]>0), CONSTRAINT [CK_Item_Type] CHECK ([Type] BETWEEN 0 AND 3),
            CONSTRAINT [CK_Item_Tracking] CHECK ([Tracking] BETWEEN 0 AND 2), CONSTRAINT [CK_Item_Reorder] CHECK ([ReorderLevel] IS NULL OR [ReorderLevel]>=0),
            CONSTRAINT [CK_Item_Expiry] CHECK ([ShelfLifeDays] IS NULL OR ([ShelfLifeDays]>0 AND [Tracking]<>0)),
            CONSTRAINT [CK_Item_Service] CHECK ([Type]<>1 OR ([Tracking]=0 AND [ShelfLifeDays] IS NULL AND [ReorderLevel] IS NULL))
        );');
        EXEC(N'CREATE UNIQUE INDEX [IX_Items_CompanyId_Code] ON [Items] ([CompanyId], [Code]);');
        EXEC(N'CREATE INDEX [IX_Items_CompanyId_GroupId] ON [Items] ([CompanyId], [GroupId]);');
        EXEC(N'CREATE INDEX [IX_Items_CompanyId_BaseUnitId] ON [Items] ([CompanyId], [BaseUnitId]);');
        EXEC(N'CREATE TABLE [ItemUnits] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [ItemId] uniqueidentifier NOT NULL, [UnitId] uniqueidentifier NOT NULL, [FactorToBase] decimal(20,6) NOT NULL,
            [Barcode] nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL, [SalePrice] decimal(20,6) NULL,
            CONSTRAINT [PK_ItemUnits] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_ItemUnits_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [FK_ItemUnits_Items_CompanyId_ItemId] FOREIGN KEY ([CompanyId], [ItemId]) REFERENCES [Items] ([CompanyId], [Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_ItemUnits_UnitsOfMeasure_CompanyId_UnitId] FOREIGN KEY ([CompanyId], [UnitId]) REFERENCES [UnitsOfMeasure] ([CompanyId], [Id]),
            CONSTRAINT [CK_ItemUnit_Factor] CHECK ([FactorToBase]>0), CONSTRAINT [CK_ItemUnit_Price] CHECK ([SalePrice] IS NULL OR [SalePrice]>=0)
        );');
        EXEC(N'CREATE UNIQUE INDEX [IX_ItemUnits_CompanyId_ItemId_UnitId] ON [ItemUnits] ([CompanyId], [ItemId], [UnitId]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_ItemUnits_CompanyId_Barcode] ON [ItemUnits] ([CompanyId], [Barcode]) WHERE [Barcode] IS NOT NULL;');
        EXEC(N'CREATE INDEX [IX_ItemUnits_CompanyId_UnitId] ON [ItemUnits] ([CompanyId], [UnitId]);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (10, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
