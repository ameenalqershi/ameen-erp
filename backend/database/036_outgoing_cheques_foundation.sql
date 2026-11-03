-- CASH08 structural-only: bank debit, cancellation and cancelled-number reuse
-- require source-approved cheque book / reconciliation policy and CASH09.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=35)
  THROW 51001,'Apply schema 035 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=36)
 BEGIN
  CREATE TABLE [OutgoingCheques](
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
   [BankAccountId] uniqueidentifier NOT NULL,
   [BeneficiaryId] uniqueidentifier NOT NULL,
   [Amount] decimal(20,6) NOT NULL,
   [DueDate] date NOT NULL,
   [ChequeState] nvarchar(32) NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_OutgoingCheques] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_OutgoingCheque_Company] FOREIGN KEY ([CompanyId])
     REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_OutgoingCheque_Branch] FOREIGN KEY ([CompanyId],[BranchId])
     REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_OutgoingCheque_Year] FOREIGN KEY ([CompanyId],[FiscalYearId])
     REFERENCES [FiscalYears]([CompanyId],[Id]),
   CONSTRAINT [FK_OutgoingCheque_Currency] FOREIGN KEY ([CompanyId],[CurrencyCode])
     REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [FK_OutgoingCheque_Bank] FOREIGN KEY ([CompanyId],[BankAccountId])
     REFERENCES [BankAccounts]([CompanyId],[Id]),
   CONSTRAINT [CK_OutgoingCheque_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_OutgoingCheque_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_OutgoingCheque_Rate] CHECK ([ExchangeRate]>0),
   CONSTRAINT [CK_OutgoingCheque_Amount] CHECK ([Amount]>0),
   CONSTRAINT [CK_OutgoingCheque_Text] CHECK (
     LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([ChequeNumber])))>0),
   CONSTRAINT [CK_OutgoingCheque_State] CHECK (
     LEN(LTRIM(RTRIM([ChequeState])))>0)
  );
  CREATE INDEX [IX_OutgoingCheques_Company_Bank_Number]
   ON [OutgoingCheques]([CompanyId],[BankAccountId],[ChequeNumber]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
    VALUES(36,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
