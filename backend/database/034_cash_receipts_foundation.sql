-- CASH03 structural-only foundation. No receipt may be saved by EF or posted
-- before approved method/target, SAL11 balance, CASH07 cheque and GL06 rules.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult < 0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=33)
  THROW 51001,'Apply schema 033 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=34)
 BEGIN
  CREATE TABLE [CashReceipts](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,
   [FiscalYearId] uniqueidentifier NOT NULL,
   [Number] nvarchar(64) NOT NULL,
   [DocumentDate] date NOT NULL,
   [AccountingDate] date NOT NULL,
   [CurrencyCode] nvarchar(3) NOT NULL,
   [ExchangeRate] decimal(20,10) NOT NULL,
   [Description] nvarchar(1000) NOT NULL,
   [ExternalReference] nvarchar(100) NULL,
   [State] nvarchar(32) NOT NULL,
   [PayerId] uniqueidentifier NOT NULL,
   [PaymentMethod] nvarchar(32) NOT NULL,
   -- A single conditional CASH03-F15 target can be cashbox or bank.
   -- Method/target matching is NOT assumed in the unapproved foundation.
   [CashBoxId] uniqueidentifier NULL,
   [BankAccountId] uniqueidentifier NULL,
   [Amount] decimal(20,6) NOT NULL,
   [ChequeReference] nvarchar(100) NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_CashReceipts] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashReceipt_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashReceipt_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_CashReceipt_Year] FOREIGN KEY ([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id]),
   CONSTRAINT [FK_CashReceipt_Currency] FOREIGN KEY ([CompanyId],[CurrencyCode]) REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [FK_CashReceipt_CashBox] FOREIGN KEY ([CompanyId],[CashBoxId]) REFERENCES [CashBoxes]([CompanyId],[Id]),
   CONSTRAINT [FK_CashReceipt_BankAccount] FOREIGN KEY ([CompanyId],[BankAccountId]) REFERENCES [BankAccounts]([CompanyId],[Id]),
   CONSTRAINT [CK_CashReceipt_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_CashReceipt_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_CashReceipt_Rate] CHECK ([ExchangeRate]>0),
   CONSTRAINT [CK_CashReceipt_Amount] CHECK ([Amount]>0),
   CONSTRAINT [CK_CashReceipt_Nonempty] CHECK (
      LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([PaymentMethod])))>0)
  );
  -- Numbering uniqueness dimensions are not invented before SYS10 receipt
  -- sequence integration; this is a lookup index only, not a uniqueness rule.
  CREATE INDEX [IX_CashReceipts_Company_Branch_Year_Number]
   ON [CashReceipts]([CompanyId],[BranchId],[FiscalYearId],[Number]);

  CREATE TABLE [CashReceiptLines](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [ReceiptId] uniqueidentifier NOT NULL,
   -- SAL11 invoices do not exist in the current physical database. No fake FK.
   [InvoiceId] uniqueidentifier NULL,
   [AllocatedAmount] decimal(20,6) NOT NULL,
   [SettlementDiscount] decimal(20,6) NOT NULL,
   CONSTRAINT [PK_CashReceiptLines] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashReceiptLine_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashReceiptLine_Receipt] FOREIGN KEY ([CompanyId],[ReceiptId]) REFERENCES [CashReceipts]([CompanyId],[Id]),
   CONSTRAINT [CK_CashReceiptLine_Amounts] CHECK ([AllocatedAmount]>=0 AND [SettlementDiscount]>=0)
  );
  CREATE INDEX [IX_CashReceiptLines_Company_Receipt]
   ON [CashReceiptLines]([CompanyId],[ReceiptId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(34,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
