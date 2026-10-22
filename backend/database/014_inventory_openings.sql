SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
 IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=13) THROW 51001, 'Apply schema 013 first', 1;
 IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=14)
 BEGIN
 CREATE TABLE [InventoryOpenings] (
  [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
  [BranchId] uniqueidentifier NOT NULL, [WarehouseId] uniqueidentifier NOT NULL,
  [FiscalYearId] uniqueidentifier NOT NULL, [FiscalPeriodId] uniqueidentifier NOT NULL,
  [Number] nvarchar(64) NOT NULL, [DocumentDate] date NOT NULL, [AccountingDate] date NOT NULL, [CutoffDate] date NOT NULL,
  [CurrencyCode] nvarchar(3) NOT NULL, [ExchangeRate] decimal(20,10) NOT NULL,
  [CountReference] nvarchar(100) NOT NULL, [Description] nvarchar(500) NOT NULL, [ExternalReference] nvarchar(100) NULL,
  [CreatedBy] nvarchar(200) NOT NULL, [ApprovedBy] nvarchar(200) NULL,
  [CreatedAt] datetimeoffset NOT NULL, [ModifiedAt] datetimeoffset NOT NULL, [Revision] int NOT NULL, [State] int NOT NULL,
  CONSTRAINT [PK_InventoryOpenings] PRIMARY KEY ([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpenings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
  CONSTRAINT [FK_InventoryOpenings_Branches_CompanyId_BranchId] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpenings_Warehouses_CompanyId_WarehouseId] FOREIGN KEY ([CompanyId],[WarehouseId]) REFERENCES [Warehouses]([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpenings_FiscalYears_CompanyId_FiscalYearId] FOREIGN KEY ([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpenings_FiscalPeriods_CompanyId_FiscalPeriodId] FOREIGN KEY ([CompanyId],[FiscalPeriodId]) REFERENCES [FiscalPeriods]([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpenings_Currencies_CompanyId_CurrencyCode] FOREIGN KEY ([CompanyId],[CurrencyCode]) REFERENCES [Currencies]([CompanyId],[Code]),
  CONSTRAINT [CK_InventoryOpening_State] CHECK ([State] BETWEEN 0 AND 4),
  CONSTRAINT [CK_InventoryOpening_Revision] CHECK ([Revision] > 0),
  CONSTRAINT [CK_InventoryOpening_Rate] CHECK ([ExchangeRate] = 1),
  CONSTRAINT [CK_InventoryOpening_Approval] CHECK (([State] = 3 AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> [CreatedBy]) OR ([State] <> 3 AND [ApprovedBy] IS NULL))
 );
 CREATE TABLE [InventoryOpeningLines] (
  [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
  [OpeningId] uniqueidentifier NOT NULL, [Position] int NOT NULL,
  [ItemId] uniqueidentifier NOT NULL, [ItemVersion] uniqueidentifier NOT NULL, [UnitId] uniqueidentifier NOT NULL,
  [IdentityId] uniqueidentifier NULL, [IdentityVersion] uniqueidentifier NULL,
  [ItemCode] nvarchar(32) NOT NULL, [UnitCode] nvarchar(32) NOT NULL, [LotNumber] nvarchar(32) NULL, [SerialNumber] nvarchar(32) NULL,
  [Quantity] decimal(20,6) NOT NULL, [FactorToBase] decimal(20,6) NOT NULL, [BaseQuantity] decimal(20,6) NOT NULL,
  [UnitCost] decimal(20,6) NOT NULL, [Value] decimal(20,6) NOT NULL,
  CONSTRAINT [PK_InventoryOpeningLines] PRIMARY KEY ([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpeningLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
  CONSTRAINT [FK_InventoryOpeningLines_InventoryOpenings_CompanyId_OpeningId] FOREIGN KEY ([CompanyId],[OpeningId]) REFERENCES [InventoryOpenings]([CompanyId],[Id]) ON DELETE CASCADE,
  CONSTRAINT [FK_InventoryOpeningLines_Items_CompanyId_ItemId] FOREIGN KEY ([CompanyId],[ItemId]) REFERENCES [Items]([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpeningLines_Units_CompanyId_UnitId] FOREIGN KEY ([CompanyId],[UnitId]) REFERENCES [UnitsOfMeasure]([CompanyId],[Id]),
  CONSTRAINT [FK_InventoryOpeningLines_InventoryIdentities_CompanyId_IdentityId] FOREIGN KEY ([CompanyId],[IdentityId]) REFERENCES [InventoryIdentities]([CompanyId],[Id]),
  CONSTRAINT [CK_InventoryOpeningLine_Values] CHECK ([Position] > 0 AND [Quantity] > 0 AND [FactorToBase] > 0 AND [BaseQuantity] > 0 AND [UnitCost] >= 0 AND [Value] >= 0)
 );
 CREATE UNIQUE INDEX [IX_InventoryOpenings_CompanyId_Number] ON [InventoryOpenings]([CompanyId],[Number]);
 CREATE INDEX [IX_InventoryOpenings_CompanyId_FiscalPeriodId_State] ON [InventoryOpenings]([CompanyId],[FiscalPeriodId],[State]);
 CREATE INDEX [IX_InventoryOpenings_CompanyId_BranchId_AccountingDate] ON [InventoryOpenings]([CompanyId],[BranchId],[AccountingDate]);
 CREATE INDEX [IX_InventoryOpenings_CompanyId_WarehouseId] ON [InventoryOpenings]([CompanyId],[WarehouseId]);
 CREATE INDEX [IX_InventoryOpenings_CompanyId_FiscalYearId] ON [InventoryOpenings]([CompanyId],[FiscalYearId]);
 CREATE INDEX [IX_InventoryOpenings_CompanyId_CurrencyCode] ON [InventoryOpenings]([CompanyId],[CurrencyCode]);
 CREATE UNIQUE INDEX [IX_InventoryOpeningLines_CompanyId_OpeningId_Position] ON [InventoryOpeningLines]([CompanyId],[OpeningId],[Position]);
 CREATE INDEX [IX_InventoryOpeningLines_CompanyId_ItemId] ON [InventoryOpeningLines]([CompanyId],[ItemId]);
 CREATE INDEX [IX_InventoryOpeningLines_CompanyId_UnitId] ON [InventoryOpeningLines]([CompanyId],[UnitId]);
 CREATE INDEX [IX_InventoryOpeningLines_CompanyId_IdentityId] ON [InventoryOpeningLines]([CompanyId],[IdentityId]);
 INSERT INTO dbo.__AmeenSchemaVersions([Version],AppliedAt) VALUES(14,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
