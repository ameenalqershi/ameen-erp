-- SAL09/SAL15 structural persistence only. No representative/customer-group
-- write API or policy is enabled. Missing provider references remain opaque GUIDs.
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
    AND (
      OBJECT_ID(N'dbo.SalesRepresentatives',N'U') IS NULL OR
      OBJECT_ID(N'dbo.CustomerGroups',N'U') IS NULL OR
      COL_LENGTH(N'dbo.SalesRepresentatives',N'CommissionPolicyId') IS NULL OR
      COL_LENGTH(N'dbo.CustomerGroups',N'PriceListId') IS NULL OR
      NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.SalesRepresentatives') AND name=N'CK_SalesRepresentative_Audit') OR
      NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CustomerGroups') AND name=N'CK_CustomerGroup_References'))
     THROW 51002,'Schema 049 belongs to another module or is incomplete; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
 BEGIN
  CREATE TABLE dbo.SalesRepresentatives(
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Name] nvarchar(200) NOT NULL,
   [EnglishName] nvarchar(200) NULL,
   [IsActive] bit NOT NULL,
   [Notes] nvarchar(2000) NULL,
   [EmployeeId] uniqueidentifier NULL,
   [Region] nvarchar(200) NOT NULL,
   [SupervisorId] uniqueidentifier NULL,
   [CustodyAccountId] uniqueidentifier NULL,
   [CommissionPolicyId] uniqueidentifier NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_SalesRepresentatives] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_SalesRepresentative_Company] FOREIGN KEY([CompanyId])
      REFERENCES dbo.Companies([Id]),
   CONSTRAINT [FK_SalesRepresentative_CustodyAccount] FOREIGN KEY([CompanyId],[CustodyAccountId])
      REFERENCES dbo.Accounts([CompanyId],[Id]),
   CONSTRAINT [CK_SalesRepresentative_Audit] CHECK
      ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt]),
   CONSTRAINT [CK_SalesRepresentative_References] CHECK(
      ([EmployeeId] IS NULL OR [EmployeeId]<>'00000000-0000-0000-0000-000000000000') AND
      ([SupervisorId] IS NULL OR [SupervisorId]<>'00000000-0000-0000-0000-000000000000') AND
      ([CustodyAccountId] IS NULL OR [CustodyAccountId]<>'00000000-0000-0000-0000-000000000000') AND
      ([CommissionPolicyId] IS NULL OR [CommissionPolicyId]<>'00000000-0000-0000-0000-000000000000'))
  );
  CREATE UNIQUE INDEX [IX_SalesRepresentatives_CompanyId_Code]
      ON dbo.SalesRepresentatives([CompanyId],[Code]);

  CREATE TABLE dbo.CustomerGroups(
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Name] nvarchar(200) NOT NULL,
   [EnglishName] nvarchar(200) NULL,
   [IsActive] bit NOT NULL,
   [Notes] nvarchar(2000) NULL,
   [ParentGroupId] uniqueidentifier NULL,
   [PriceListId] uniqueidentifier NOT NULL,
   [DefaultCreditLimit] decimal(20,6) NOT NULL,
   [CreditCurrencyId] uniqueidentifier NOT NULL,
   [PaymentTermId] uniqueidentifier NOT NULL,
   [ControlAccountId] uniqueidentifier NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_CustomerGroups] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_CustomerGroup_Company] FOREIGN KEY([CompanyId])
      REFERENCES dbo.Companies([Id]),
   CONSTRAINT [FK_CustomerGroup_Parent] FOREIGN KEY([CompanyId],[ParentGroupId])
      REFERENCES dbo.CustomerGroups([CompanyId],[Id]),
   CONSTRAINT [FK_CustomerGroup_Currency] FOREIGN KEY([CompanyId],[CreditCurrencyId])
      REFERENCES dbo.Currencies([CompanyId],[Id]),
   CONSTRAINT [FK_CustomerGroup_PaymentTerm] FOREIGN KEY([CompanyId],[PaymentTermId])
      REFERENCES dbo.PaymentTerms([CompanyId],[Id]),
   CONSTRAINT [FK_CustomerGroup_ControlAccount] FOREIGN KEY([CompanyId],[ControlAccountId])
      REFERENCES dbo.Accounts([CompanyId],[Id]),
   CONSTRAINT [CK_CustomerGroup_Audit] CHECK
      ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt]),
   CONSTRAINT [CK_CustomerGroup_Credit] CHECK([DefaultCreditLimit]>=0),
   CONSTRAINT [CK_CustomerGroup_References] CHECK(
      [PriceListId]<>'00000000-0000-0000-0000-000000000000' AND
      [CreditCurrencyId]<>'00000000-0000-0000-0000-000000000000' AND
      [PaymentTermId]<>'00000000-0000-0000-0000-000000000000' AND
      [ControlAccountId]<>'00000000-0000-0000-0000-000000000000' AND
      ([ParentGroupId] IS NULL OR [ParentGroupId]<>'00000000-0000-0000-0000-000000000000'))
  );
  CREATE UNIQUE INDEX [IX_CustomerGroups_CompanyId_Code]
      ON dbo.CustomerGroups([CompanyId],[Code]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
      VALUES(49,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
