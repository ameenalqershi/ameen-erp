-- CASH06-01: structural-only advance and settlement-line foundation.
-- HR03 holder/expense definitions, type/state enumerations and financial
-- settlement/reimbursement authority are not approved. EF writes fail closed.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
   @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=38)
   THROW 51001,'Apply schema 038 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=39)
    AND (OBJECT_ID(N'dbo.CashAdvances',N'U') IS NULL
      OR OBJECT_ID(N'dbo.CashAdvanceLines',N'U') IS NULL)
   THROW 51002,'Schema 039 belongs to another module; rebase and renumber CASH06 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=39)
 BEGIN
  CREATE TABLE [CashAdvances](
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
   -- HR03 holder is not an approved foreign-key target yet.
   [HolderId] uniqueidentifier NOT NULL,
   [AdvanceType] nvarchar(32) NOT NULL,
   [Amount] decimal(20,6) NOT NULL,
   [ExpectedSettlementDate] date NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_CashAdvances] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashAdvance_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashAdvance_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_CashAdvance_Year] FOREIGN KEY ([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id]),
   CONSTRAINT [FK_CashAdvance_Currency] FOREIGN KEY ([CompanyId],[CurrencyCode]) REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [CK_CashAdvance_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_CashAdvance_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_CashAdvance_Rate] CHECK ([ExchangeRate]>0),
   CONSTRAINT [CK_CashAdvance_Amount] CHECK ([Amount]>0),
   CONSTRAINT [CK_CashAdvance_Text] CHECK (
      LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([AdvanceType])))>0)
  );
  -- SYS10 has not specified the advance-numbering uniqueness scope.
  CREATE INDEX [IX_CashAdvances_Company_Branch_Year_Number]
   ON [CashAdvances]([CompanyId],[BranchId],[FiscalYearId],[Number]);
  CREATE TABLE [CashAdvanceLines](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [AdvanceId] uniqueidentifier NOT NULL,
   -- The original 'expense' reference does not identify an approved target.
   [ExpenseReferenceId] uniqueidentifier NULL,
   [SettlementAmount] decimal(20,6) NULL,
   [AttachmentReference] nvarchar(300) NULL,
   CONSTRAINT [PK_CashAdvanceLines] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashAdvanceLine_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashAdvanceLine_Advance] FOREIGN KEY ([CompanyId],[AdvanceId])
      REFERENCES [CashAdvances]([CompanyId],[Id]),
   CONSTRAINT [CK_CashAdvanceLine_Settlement] CHECK (
       [SettlementAmount] IS NULL OR [SettlementAmount]>=0)
  );
  CREATE INDEX [IX_CashAdvanceLines_Company_Advance]
   ON [CashAdvanceLines]([CompanyId],[AdvanceId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
    VALUES(39,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
