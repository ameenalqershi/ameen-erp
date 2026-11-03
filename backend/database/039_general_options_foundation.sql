-- SYS11-01 structure only. Codes remain opaque pending an approved catalogue.
-- No automatic accounting mapping or posting is activated by this migration.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=38)
     THROW 51001,'Apply schema 038 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=39)
    AND (OBJECT_ID(N'dbo.GeneralOptionsPolicies',N'U') IS NULL
       OR OBJECT_ID(N'dbo.GeneralOptionsAccountLines',N'U') IS NULL)
     THROW 51002,'Schema 039 belongs to another module; rebase and renumber SYS11 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=39)
 BEGIN
  CREATE TABLE [GeneralOptionsPolicies] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL, [StatusCode] nvarchar(32) NOT NULL,
   [InventoryValuationCode] nvarchar(64) NOT NULL, [BlockNegativeStock] bit NOT NULL,
   [SalesDeliveryModeCode] nvarchar(64) NOT NULL, [QuantityPrecision] int NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL, [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL, [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_GeneralOptionsPolicies] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_GeneralOptions_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_GeneralOptions_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_GeneralOptions_Precision] CHECK ([QuantityPrecision]>=0),
   CONSTRAINT [CK_GeneralOptions_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE TABLE [GeneralOptionsAccountLines] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
   [PolicyId] uniqueidentifier NOT NULL, [EventCode] nvarchar(64) NOT NULL,
   [AccountRoleCode] nvarchar(64) NOT NULL, [AccountId] uniqueidentifier NOT NULL,
   [Scope] nvarchar(200) NOT NULL,
   CONSTRAINT [PK_GeneralOptionsAccountLines] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_GeneralOptionsLine_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_GeneralOptionsLine_Policy] FOREIGN KEY ([CompanyId],[PolicyId])
       REFERENCES [GeneralOptionsPolicies]([CompanyId],[Id]),
   CONSTRAINT [FK_GeneralOptionsLine_Account] FOREIGN KEY ([CompanyId],[AccountId])
       REFERENCES [Accounts]([CompanyId],[Id])
  );
  CREATE INDEX [IX_GeneralOptionsAccountLines_Policy]
      ON [GeneralOptionsAccountLines]([CompanyId],[PolicyId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(39,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
