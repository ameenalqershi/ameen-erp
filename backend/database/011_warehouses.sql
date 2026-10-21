SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 10) THROW 51001, 'Apply schema 010 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 11)
    BEGIN
        EXEC(N'CREATE TABLE [Warehouses] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL, [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [BranchId] uniqueidentifier NOT NULL, [KeeperMembershipId] uniqueidentifier NOT NULL, [InventoryAccountId] uniqueidentifier NOT NULL,
            [Type] int NOT NULL, [TrackLocations] bit NOT NULL, [IsActive] bit NOT NULL, [Revision] int NOT NULL,
            CONSTRAINT [PK_Warehouses] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_Warehouses_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [FK_Warehouses_Branches_CompanyId_BranchId] FOREIGN KEY ([CompanyId], [BranchId]) REFERENCES [Branches] ([CompanyId], [Id]),
            CONSTRAINT [FK_Warehouses_Memberships_CompanyId_KeeperMembershipId] FOREIGN KEY ([CompanyId], [KeeperMembershipId]) REFERENCES [Memberships] ([CompanyId], [Id]),
            CONSTRAINT [FK_Warehouses_Accounts_CompanyId_InventoryAccountId] FOREIGN KEY ([CompanyId], [InventoryAccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
            CONSTRAINT [CK_Warehouse_Revision] CHECK ([Revision]>0), CONSTRAINT [CK_Warehouse_Type] CHECK ([Type] BETWEEN 0 AND 1)
        );');
        EXEC(N'CREATE UNIQUE INDEX [IX_Warehouses_CompanyId_Code] ON [Warehouses] ([CompanyId], [Code]);');
        EXEC(N'CREATE INDEX [IX_Warehouses_CompanyId_BranchId] ON [Warehouses] ([CompanyId], [BranchId]);');
        EXEC(N'CREATE INDEX [IX_Warehouses_CompanyId_KeeperMembershipId] ON [Warehouses] ([CompanyId], [KeeperMembershipId]);');
        EXEC(N'CREATE INDEX [IX_Warehouses_CompanyId_InventoryAccountId] ON [Warehouses] ([CompanyId], [InventoryAccountId]);');
        EXEC(N'CREATE TABLE [WarehouseLocations] (
            [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
            [WarehouseId] uniqueidentifier NOT NULL, [Code] nvarchar(32) NOT NULL, [Description] nvarchar(500) NOT NULL,
            CONSTRAINT [PK_WarehouseLocations] PRIMARY KEY ([CompanyId], [Id]),
            CONSTRAINT [FK_WarehouseLocations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
            CONSTRAINT [FK_WarehouseLocations_Warehouses_CompanyId_WarehouseId] FOREIGN KEY ([CompanyId], [WarehouseId]) REFERENCES [Warehouses] ([CompanyId], [Id]) ON DELETE CASCADE
        );');
        EXEC(N'CREATE UNIQUE INDEX [IX_WarehouseLocations_CompanyId_WarehouseId_Code] ON [WarehouseLocations] ([CompanyId], [WarehouseId], [Code]);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (11, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
