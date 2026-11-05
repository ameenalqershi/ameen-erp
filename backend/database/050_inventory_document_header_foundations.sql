-- INV07/08/09/10/13/14/15-FG01: isolated header-scope foundations only.
-- No source type, document lifecycle, approval, stock movement, valuation or GL posting.
-- INV11 is excluded because its required SalesOrder provider is not ready.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
  THROW 51001,'Apply schema 049 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=50)
    AND (
      OBJECT_ID(N'dbo.InventoryReceiptHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryIssueHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryTransferHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryTransferReceiptHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryCountHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryAdjustmentHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryDamageHeaders',N'U') IS NULL OR
      OBJECT_ID(N'dbo.InventoryRevaluationHeaders',N'U') IS NULL)
  THROW 51002,'Schema 050 is incomplete or belongs to another module',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=50)
 BEGIN
  CREATE TABLE [InventoryReceiptHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryReceiptHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryReceiptHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryReceiptHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryReceiptHeaders_FiscalYear] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id])
  );
  CREATE TABLE [InventoryIssueHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryIssueHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryIssueHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryIssueHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryIssueHeaders_FiscalYear] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id])
  );
  CREATE TABLE [InventoryTransferHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryTransferHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryTransferHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryTransferHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryTransferHeaders_FiscalYear] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id])
  );
  CREATE TABLE [InventoryTransferReceiptHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryTransferReceiptHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryTransferReceiptHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryTransferReceiptHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryTransferReceiptHeaders_FiscalYear] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id])
  );
  CREATE TABLE [InventoryAdjustmentHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryAdjustmentHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryAdjustmentHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryAdjustmentHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryAdjustmentHeaders_FiscalYear] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id])
  );
  CREATE TABLE [InventoryDamageHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryDamageHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryDamageHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryDamageHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryDamageHeaders_FiscalYear] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id])
  );
  CREATE TABLE [InventoryCountHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,[StatusCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [WarehouseId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_InventoryCountHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryCountHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryCountHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryCountHeaders_Warehouse] FOREIGN KEY([CompanyId],[WarehouseId]) REFERENCES [Warehouses]([CompanyId],[Id]),
   CONSTRAINT [CK_InventoryCountHeader_Status] CHECK(LEN(LTRIM(RTRIM([StatusCode])))>0),
   CONSTRAINT [CK_InventoryCountHeader_References] CHECK([WarehouseId]<>'00000000-0000-0000-0000-000000000000' AND ([BranchId] IS NULL OR [BranchId]<>'00000000-0000-0000-0000-000000000000'))
  );
  CREATE TABLE [InventoryRevaluationHeaders](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,[StatusCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [FromDate] date NOT NULL,
   CONSTRAINT [PK_InventoryRevaluationHeaders] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryRevaluationHeaders_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryRevaluationHeaders_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_InventoryRevaluationHeader_Status] CHECK(LEN(LTRIM(RTRIM([StatusCode])))>0),
   CONSTRAINT [CK_InventoryRevaluationHeader_FromDate] CHECK([FromDate]>'0001-01-01' AND ([BranchId] IS NULL OR [BranchId]<>'00000000-0000-0000-0000-000000000000'))
  );
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(50,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
