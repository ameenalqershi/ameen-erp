-- SAL02 structural price-list persistence only. No priority resolution,
-- overlap arbitration, discount authorization, invoice mutation or write API.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
     @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
     THROW 51001,'Apply schema 049 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=50)
    AND (
      OBJECT_ID(N'dbo.SalesPriceLists',N'U') IS NULL OR
      OBJECT_ID(N'dbo.SalesPriceListLines',N'U') IS NULL OR
      COL_LENGTH(N'dbo.SalesPriceLists',N'EffectiveTo') IS NULL OR
      COL_LENGTH(N'dbo.SalesPriceListLines',N'QuantityThreshold') IS NULL OR
      NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_CustomerGroup_PriceList' AND parent_object_id=OBJECT_ID(N'dbo.CustomerGroups')) OR
      NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SalesPriceListLine_ItemUnit' AND parent_object_id=OBJECT_ID(N'dbo.SalesPriceListLines')) OR
      NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SalesPriceListLine_PriceList' AND parent_object_id=OBJECT_ID(N'dbo.SalesPriceListLines')) OR
      NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_SalesPriceListLine_Amounts' AND parent_object_id=OBJECT_ID(N'dbo.SalesPriceListLines')))
     THROW 51002,'Schema 050 belongs to another module or is incomplete; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=50)
 BEGIN
  CREATE TABLE dbo.SalesPriceLists(
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,
   [Status] nvarchar(64) NOT NULL,
   [Name] nvarchar(200) NOT NULL,
   [CurrencyId] uniqueidentifier NOT NULL,
   [EffectiveFrom] date NOT NULL,
   [EffectiveTo] date NULL,
   [IncludesTax] bit NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_SalesPriceLists] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_SalesPriceList_Company] FOREIGN KEY([CompanyId])
      REFERENCES dbo.Companies([Id]),
   CONSTRAINT [FK_SalesPriceList_Branch] FOREIGN KEY([CompanyId],[BranchId])
      REFERENCES dbo.Branches([CompanyId],[Id]),
   CONSTRAINT [FK_SalesPriceList_Currency] FOREIGN KEY([CompanyId],[CurrencyId])
      REFERENCES dbo.Currencies([CompanyId],[Id]),
   CONSTRAINT [CK_SalesPriceList_Audit] CHECK([Revision]>0 AND [LastModifiedAt]>=[CreatedAt]),
   CONSTRAINT [CK_SalesPriceList_Dates] CHECK([EffectiveTo] IS NULL OR [EffectiveTo]>=[EffectiveFrom]),
   CONSTRAINT [CK_SalesPriceList_References] CHECK(
      [CurrencyId]<>'00000000-0000-0000-0000-000000000000' AND
      ([BranchId] IS NULL OR [BranchId]<>'00000000-0000-0000-0000-000000000000'))
  );

  CREATE TABLE dbo.SalesPriceListLines(
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [PriceListId] uniqueidentifier NOT NULL,
   [Position] int NOT NULL,
   [ItemId] uniqueidentifier NOT NULL,
   [UnitId] uniqueidentifier NOT NULL,
   [Price] decimal(20,6) NOT NULL,
   [DiscountLimit] decimal(20,6) NOT NULL,
   [QuantityThreshold] decimal(20,6) NULL,
   CONSTRAINT [PK_SalesPriceListLines] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_SalesPriceListLine_Company] FOREIGN KEY([CompanyId])
      REFERENCES dbo.Companies([Id]),
   CONSTRAINT [FK_SalesPriceListLine_PriceList] FOREIGN KEY([CompanyId],[PriceListId])
      REFERENCES dbo.SalesPriceLists([CompanyId],[Id]),
   CONSTRAINT [FK_SalesPriceListLine_ItemUnit] FOREIGN KEY([CompanyId],[ItemId],[UnitId])
      REFERENCES dbo.ItemUnits([CompanyId],[ItemId],[UnitId]),
   CONSTRAINT [CK_SalesPriceListLine_Position] CHECK([Position]>0),
   CONSTRAINT [CK_SalesPriceListLine_Amounts] CHECK(
      [Price]>=0 AND [DiscountLimit]>=0 AND
      ([QuantityThreshold] IS NULL OR [QuantityThreshold]>=0)),
   CONSTRAINT [CK_SalesPriceListLine_References] CHECK(
      [PriceListId]<>'00000000-0000-0000-0000-000000000000' AND
      [ItemId]<>'00000000-0000-0000-0000-000000000000' AND
      [UnitId]<>'00000000-0000-0000-0000-000000000000')
  );
  CREATE UNIQUE INDEX [IX_SalesPriceListLines_CompanyId_PriceListId_Position]
      ON dbo.SalesPriceListLines([CompanyId],[PriceListId],[Position]);

  ALTER TABLE dbo.CustomerGroups WITH CHECK
      ADD CONSTRAINT [FK_CustomerGroup_PriceList]
      FOREIGN KEY([CompanyId],[PriceListId])
      REFERENCES dbo.SalesPriceLists([CompanyId],[Id]);

  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
      VALUES(50,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
