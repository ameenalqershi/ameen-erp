-- CASH05-01: schema-only cashbox/bank transfer header. Conditional company-scoped
-- FKs enforce each typed endpoint. No FX settlement, approval, posting or EF writes.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
     @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=48)
    THROW 51001,'Apply schema 048 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
    AND OBJECT_ID(N'dbo.CashTransfers',N'U') IS NULL
    THROW 51002,'Schema 049 belongs to another module; rebase and renumber CASH05',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
 BEGIN
  CREATE TABLE [CashTransfers] (
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
   [SourceKind] int NOT NULL,
   [SourceAccountId] uniqueidentifier NOT NULL,
   [DestinationKind] int NOT NULL,
   [DestinationAccountId] uniqueidentifier NOT NULL,
   [SourceAmount] decimal(20,6) NOT NULL,
   [DestinationAmount] decimal(20,6) NOT NULL,
   [DestinationCurrencyCode] nvarchar(3) NOT NULL,
   [DestinationExchangeRate] decimal(20,10) NOT NULL,
   [Fee] decimal(20,6) NULL,
   -- Null-inactive aliases are technical FKs, never posted accounts.
   [SourceCashBoxId] uniqueidentifier NULL,
   [SourceBankAccountId] uniqueidentifier NULL,
   [DestinationCashBoxId] uniqueidentifier NULL,
   [DestinationBankAccountId] uniqueidentifier NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_CashTransfers] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_CashTransfer_Company] FOREIGN KEY([CompanyId])
     REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashTransfer_Branch] FOREIGN KEY([CompanyId],[BranchId])
     REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_CashTransfer_Year] FOREIGN KEY([CompanyId],[FiscalYearId])
     REFERENCES [FiscalYears]([CompanyId],[Id]),
   CONSTRAINT [FK_CashTransfer_Currency] FOREIGN KEY([CompanyId],[CurrencyCode])
     REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [FK_CashTransfer_DestinationCurrency] FOREIGN KEY([CompanyId],[DestinationCurrencyCode])
     REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [FK_CashTransfer_SourceCashBox] FOREIGN KEY([CompanyId],[SourceCashBoxId])
     REFERENCES [CashBoxes]([CompanyId],[Id]),
   CONSTRAINT [FK_CashTransfer_SourceBank] FOREIGN KEY([CompanyId],[SourceBankAccountId])
     REFERENCES [BankAccounts]([CompanyId],[Id]),
   CONSTRAINT [FK_CashTransfer_DestinationCashBox] FOREIGN KEY([CompanyId],[DestinationCashBoxId])
     REFERENCES [CashBoxes]([CompanyId],[Id]),
   CONSTRAINT [FK_CashTransfer_DestinationBank] FOREIGN KEY([CompanyId],[DestinationBankAccountId])
     REFERENCES [BankAccounts]([CompanyId],[Id]),
   CONSTRAINT [CK_CashTransfer_Revision] CHECK
     ([Revision]>0 AND [CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_CashTransfer_Money] CHECK
     ([SourceAmount]>0 AND [DestinationAmount]>0 AND ([Fee] IS NULL OR [Fee]>=0)),
   CONSTRAINT [CK_CashTransfer_Rates] CHECK
     ([ExchangeRate]>0 AND [DestinationExchangeRate]>0),
   CONSTRAINT [CK_CashTransfer_Draft] CHECK ([State]=N'Draft'),
   CONSTRAINT [CK_CashTransfer_Accounts] CHECK (
      (([SourceKind]=0 AND [SourceCashBoxId] IS NOT NULL
         AND [SourceCashBoxId]=[SourceAccountId] AND [SourceBankAccountId] IS NULL)
        OR ([SourceKind]=1 AND [SourceBankAccountId] IS NOT NULL
         AND [SourceBankAccountId]=[SourceAccountId] AND [SourceCashBoxId] IS NULL))
      AND (([DestinationKind]=0 AND [DestinationCashBoxId] IS NOT NULL
         AND [DestinationCashBoxId]=[DestinationAccountId] AND [DestinationBankAccountId] IS NULL)
        OR ([DestinationKind]=1 AND [DestinationBankAccountId] IS NOT NULL
         AND [DestinationBankAccountId]=[DestinationAccountId] AND [DestinationCashBoxId] IS NULL))
      AND ([SourceKind]<>[DestinationKind] OR [SourceAccountId]<>[DestinationAccountId])
   )
  );
  -- Not unique until SYS10 document-number scope has an approved contract.
  CREATE INDEX [IX_CashTransfers_Company_Branch_Year_Number]
    ON [CashTransfers]([CompanyId],[BranchId],[FiscalYearId],[Number]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(49,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
