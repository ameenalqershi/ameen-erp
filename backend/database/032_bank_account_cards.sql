-- CASH02 structural foundation only: writes fail closed until bank-account
-- validation, beneficiary-change review, authorization and API are verified.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult < 0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=31)
  THROW 51001,'Apply schema 031 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=32)
 BEGIN
  CREATE TABLE [BankAccounts](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [Code] nvarchar(32) NOT NULL,
   [Name] nvarchar(200) NOT NULL,
   [EnglishName] nvarchar(200) NULL,
   [IsActive] bit NOT NULL,
   [Notes] nvarchar(2000) NULL,
   [BankName] nvarchar(200) NOT NULL,
   [AccountNumber] nvarchar(64) NOT NULL,
   [Iban] nvarchar(34) NULL,
   [LedgerAccountId] uniqueidentifier NOT NULL,
   [CurrencyCode] nvarchar(3) NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_BankAccounts] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_BankAccount_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_BankAccount_Account] FOREIGN KEY ([CompanyId],[LedgerAccountId]) REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [FK_BankAccount_Currency] FOREIGN KEY ([CompanyId],[CurrencyCode]) REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [FK_BankAccount_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_BankAccount_Revision] CHECK ([Revision] > 0),
   CONSTRAINT [CK_BankAccount_AuditTime] CHECK ([CreatedAt] <= [LastModifiedAt]),
   CONSTRAINT [CK_BankAccount_IbanLength] CHECK ([Iban] IS NULL OR LEN([Iban]) BETWEEN 15 AND 34)
  );
  CREATE UNIQUE INDEX [IX_BankAccounts_CompanyId_Code] ON [BankAccounts]([CompanyId],[Code]);
  CREATE UNIQUE INDEX [IX_BankAccounts_CompanyId_BankName_AccountNumber]
   ON [BankAccounts]([CompanyId],[BankName],[AccountNumber]);
  CREATE UNIQUE INDEX [IX_BankAccounts_CompanyId_Iban] ON [BankAccounts]([CompanyId],[Iban])
   WHERE [Iban] IS NOT NULL;
  CREATE INDEX [IX_BankAccounts_CompanyId_BranchId] ON [BankAccounts]([CompanyId],[BranchId]);
  CREATE INDEX [IX_BankAccounts_CompanyId_LedgerAccountId] ON [BankAccounts]([CompanyId],[LedgerAccountId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(32,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
