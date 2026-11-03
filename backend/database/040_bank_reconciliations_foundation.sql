-- CASH10-01 structural foundation only. No source bank statement movement
-- master table or approved ledger movement resolver currently exists. These
-- references are GUID placeholders, not fabricated foreign keys or operations.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=39)
  THROW 51001,'Apply schema 039 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=40)
    AND (OBJECT_ID(N'dbo.BankReconciliations',N'U') IS NULL
      OR OBJECT_ID(N'dbo.BankReconciliationLines',N'U') IS NULL)
  THROW 51002,'Schema 040 belongs to another module; rebase and renumber CASH10 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=40)
 BEGIN
  CREATE TABLE [BankReconciliations](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,
   [State] nvarchar(32) NOT NULL,
   [BankAccountId] uniqueidentifier NOT NULL,
   [StatementFrom] date NOT NULL,
   [StatementTo] date NOT NULL,
   [OpeningBalance] decimal(20,6) NOT NULL,
   [ClosingBalance] decimal(20,6) NOT NULL,
   [StatementFileId] nvarchar(300) NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_BankReconciliations] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_BankReconciliation_Company] FOREIGN KEY ([CompanyId])
     REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_BankReconciliation_Branch] FOREIGN KEY ([CompanyId],[BranchId])
     REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_BankReconciliation_Bank] FOREIGN KEY ([CompanyId],[BankAccountId])
     REFERENCES [BankAccounts]([CompanyId],[Id]),
   CONSTRAINT [CK_BankReconciliation_Dates] CHECK ([StatementFrom]<=[StatementTo]),
   CONSTRAINT [CK_BankReconciliation_Audit] CHECK ([Revision]>0 AND [CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_BankReconciliation_State] CHECK (LEN(LTRIM(RTRIM([State])))>0)
  );
  -- A statement range is a lookup only: dedup scope needs approved import key.
  CREATE INDEX [IX_BankReconciliations_Company_Bank_Dates]
     ON [BankReconciliations]([CompanyId],[BankAccountId],[StatementFrom],[StatementTo]);
  CREATE TABLE [BankReconciliationLines](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [ReconciliationId] uniqueidentifier NOT NULL,
   [BankMovementId] uniqueidentifier NOT NULL,
   [LedgerMovementId] uniqueidentifier NULL,
   [MatchedAmount] decimal(20,6) NOT NULL,
   [VarianceReason] nvarchar(1000) NULL,
   CONSTRAINT [PK_BankReconciliationLines] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_BankReconciliationLine_Company] FOREIGN KEY ([CompanyId])
     REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_BankReconciliationLine_Parent] FOREIGN KEY ([CompanyId],[ReconciliationId])
     REFERENCES [BankReconciliations]([CompanyId],[Id]),
   CONSTRAINT [CK_BankReconciliationLine_Amount] CHECK ([MatchedAmount]>=0)
  );
  -- Multiple lines for the same bank/ledger movement are permitted structurally
  -- because the original spec requires partial and many-to-one matching.
  CREATE INDEX [IX_BankReconciliationLines_Company_Parent_BankMovement]
     ON [BankReconciliationLines]([CompanyId],[ReconciliationId],[BankMovementId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
     VALUES(40,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
