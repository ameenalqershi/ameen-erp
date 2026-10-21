-- INV02 item classification and accounting links. No item, tax or stock posting yet.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 8)
        THROW 51001, 'Apply schema 008 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 9)
    BEGIN
        CREATE TABLE [ItemGroups] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL,
            [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [ParentId] uniqueidentifier NULL, [SalesAccountId] uniqueidentifier NOT NULL,
            [InventoryAccountId] uniqueidentifier NULL, [CostAccountId] uniqueidentifier NULL,
            [IsActive] bit NOT NULL, [Revision] int NOT NULL,
            CONSTRAINT [PK_ItemGroups] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_ItemGroups_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [FK_ItemGroups_ItemGroups_CompanyId_ParentId] FOREIGN KEY ([CompanyId], [ParentId]) REFERENCES [ItemGroups] ([CompanyId], [Id]),
            CONSTRAINT [FK_ItemGroups_Accounts_CompanyId_SalesAccountId] FOREIGN KEY ([CompanyId], [SalesAccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
            CONSTRAINT [FK_ItemGroups_Accounts_CompanyId_InventoryAccountId] FOREIGN KEY ([CompanyId], [InventoryAccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
            CONSTRAINT [FK_ItemGroups_Accounts_CompanyId_CostAccountId] FOREIGN KEY ([CompanyId], [CostAccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
            CONSTRAINT [CK_ItemGroup_Revision] CHECK ([Revision] > 0)
        );
        CREATE UNIQUE INDEX [IX_ItemGroups_CompanyId_Code] ON [ItemGroups] ([CompanyId], [Code]);
        CREATE INDEX [IX_ItemGroups_CompanyId_ParentId] ON [ItemGroups] ([CompanyId], [ParentId]);
        CREATE INDEX [IX_ItemGroups_CompanyId_SalesAccountId] ON [ItemGroups] ([CompanyId], [SalesAccountId]);
        CREATE INDEX [IX_ItemGroups_CompanyId_InventoryAccountId] ON [ItemGroups] ([CompanyId], [InventoryAccountId]);
        CREATE INDEX [IX_ItemGroups_CompanyId_CostAccountId] ON [ItemGroups] ([CompanyId], [CostAccountId]);
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (9, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
