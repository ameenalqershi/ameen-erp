-- CASH07 structural-only: no collection/return, receivable reopening or bank
-- credit. The uniqueness scope by bank/payer and transition policy are pending.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=34)
  THROW 51001,'Apply schema 034 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=35)
 BEGIN
  CREATE TABLE [IncomingCheques](
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
   [ChequeNumber] nvarchar(64) NOT NULL,
   [DrawnBankName] nvarchar(200) NOT NULL,
   [PayerId] uniqueidentifier NOT NULL,
   [Amount] decimal(20,6) NOT NULL,
   [DueDate] date NOT NULL,
   [CollectionAccountId] uniqueidentifier NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_IncomingCheques] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_IncomingCheque_Company] FOREIGN KEY ([CompanyId])
     REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_IncomingCheque_Branch] FOREIGN KEY ([CompanyId],[BranchId])
     REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_IncomingCheque_Year] FOREIGN KEY ([CompanyId],[FiscalYearId])
     REFERENCES [FiscalYears]([CompanyId],[Id]),
   CONSTRAINT [FK_IncomingCheque_Currency] FOREIGN KEY ([CompanyId],[CurrencyCode])
     REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [FK_IncomingCheque_CollectionAccount]
     FOREIGN KEY ([CompanyId],[CollectionAccountId])
     REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [CK_IncomingCheque_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_IncomingCheque_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_IncomingCheque_Rate] CHECK ([ExchangeRate]>0),
   CONSTRAINT [CK_IncomingCheque_Amount] CHECK ([Amount]>0),
   CONSTRAINT [CK_IncomingCheque_Text] CHECK (
     LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([ChequeNumber])))>0)
  );
  -- Lookup, not uniqueness; bank and payer check-number scoping requires an
  -- explicit policy (and reconciliation of cancelled/rejected history).
  CREATE INDEX [IX_IncomingCheques_Company_Bank_Payer_Number]
   ON [IncomingCheques]([CompanyId],[DrawnBankName],[PayerId],[ChequeNumber]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
    VALUES(35,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
