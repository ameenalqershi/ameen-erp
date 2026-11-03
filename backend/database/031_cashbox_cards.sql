-- CASH01: cash custody master cards; no journal or available-balance mutation.
-- Existing company/account/branch/membership/currency rows are not changed.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @lockResult < 0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=30)
    THROW 51001,'Apply schema 030 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=31)
 BEGIN
  CREATE TABLE [CashBoxes](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [Code] nvarchar(32) NOT NULL,
   [Name] nvarchar(200) NOT NULL,
   [EnglishName] nvarchar(200) NULL,
   [IsActive] bit NOT NULL,
   [Notes] nvarchar(2000) NULL,
   [AccountId] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL,
   [CustodianMembershipId] uniqueidentifier NOT NULL,
   [CurrencyCode] nvarchar(3) NOT NULL,
   [BalanceLimit] decimal(20,6) NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_CashBoxes] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashBox_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashBox_Account] FOREIGN KEY ([CompanyId],[AccountId]) REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [FK_CashBox_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_CashBox_Custodian] FOREIGN KEY ([CompanyId],[CustodianMembershipId]) REFERENCES [Memberships]([CompanyId],[Id]),
   CONSTRAINT [FK_CashBox_Currency] FOREIGN KEY ([CompanyId],[CurrencyCode]) REFERENCES [Currencies]([CompanyId],[Code]),
   CONSTRAINT [CK_CashBox_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_CashBox_BalanceLimit] CHECK ([BalanceLimit] IS NULL OR [BalanceLimit]>=0),
   CONSTRAINT [CK_CashBox_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt])
  );
  CREATE UNIQUE INDEX [IX_CashBoxes_CompanyId_Code] ON [CashBoxes]([CompanyId],[Code]);
  CREATE UNIQUE INDEX [IX_CashBoxes_CompanyId_AccountId_BranchId_CurrencyCode]
      ON [CashBoxes]([CompanyId],[AccountId],[BranchId],[CurrencyCode]);
  CREATE INDEX [IX_CashBoxes_CompanyId_BranchId] ON [CashBoxes]([CompanyId],[BranchId]);
  CREATE INDEX [IX_CashBoxes_CompanyId_CustodianMembershipId]
      ON [CashBoxes]([CompanyId],[CustodianMembershipId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(31,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
