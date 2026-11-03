-- SYS16-01: structural foundation only. No payment operation is activated.
-- Card settlement and cash posting remain separate; no clearing transaction is inferred.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=32) THROW 51001,'Apply schema 032 first',1;
 -- Multiple module branches must not claim the same schema version.
 -- A successful version row with missing SYS16 tables is an ownership
 -- collision, not evidence that the payment migration already ran.
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=33)
    AND (OBJECT_ID(N'dbo.PaymentTerms',N'U') IS NULL
         OR OBJECT_ID(N'dbo.PaymentTermLines',N'U') IS NULL)
    THROW 51002,'Schema 033 belongs to another module; rebase and renumber SYS16 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=33)
 BEGIN
  CREATE TABLE [PaymentTerms] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL, [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL,
   [Kind] int NOT NULL, [SettlementAccountId] uniqueidentifier NULL, [GraceDays] int NOT NULL,
   [IsActive] bit NOT NULL, [CreatedBy] nvarchar(200) NOT NULL, [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL, [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_PaymentTerms] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_PaymentTerm_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_PaymentTerm_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_PaymentTerm_Account] FOREIGN KEY ([CompanyId],[SettlementAccountId]) REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [CK_PaymentTerm_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
   CONSTRAINT [CK_PaymentTerm_Grace] CHECK ([GraceDays] >= 0),
   CONSTRAINT [CK_PaymentTerm_Audit] CHECK ([Revision] > 0 AND [LastModifiedAt] >= [CreatedAt])
  );
  CREATE UNIQUE INDEX [IX_PaymentTerms_CompanyId_Code] ON [PaymentTerms]([CompanyId],[Code]);
  CREATE TABLE [PaymentTermLines] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
   [TermId] uniqueidentifier NOT NULL, [Position] int NOT NULL,
   [Percent] decimal(9,6) NOT NULL, [DaysFromBase] int NOT NULL,
   CONSTRAINT [PK_PaymentTermLines] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_PaymentTermLine_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_PaymentTermLine_Term] FOREIGN KEY ([CompanyId],[TermId]) REFERENCES [PaymentTerms]([CompanyId],[Id]),
   CONSTRAINT [CK_PaymentTermLine_Percent] CHECK ([Percent] > 0 AND [Percent] <= 100),
   CONSTRAINT [CK_PaymentTermLine_Days] CHECK ([DaysFromBase] >= 0 AND [Position] > 0)
  );
  CREATE UNIQUE INDEX [IX_PaymentTermLines_CompanyId_TermId_Position]
    ON [PaymentTermLines]([CompanyId],[TermId],[Position]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(33,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
