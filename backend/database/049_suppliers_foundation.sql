-- PUR01-01: supplier-card schema only, no bank-review, country/tax,
-- procurement document, payables or posting policy activated.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @result int;
 EXEC @result=sys.sp_getapplock @Resource=N'ameen:schema',
    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @result<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=48)
    THROW 51001,'Apply schema 048 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
    AND OBJECT_ID(N'dbo.Suppliers',N'U') IS NULL
    THROW 51002,'Schema 049 belongs to another module; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
 BEGIN
  CREATE TABLE [Suppliers](
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Name] nvarchar(200) NOT NULL, [EnglishName] nvarchar(200) NULL,
   [IsActive] bit NOT NULL, [Notes] nvarchar(2000) NULL,
   [LegalName] nvarchar(200) NOT NULL,
   [ControlAccountId] uniqueidentifier NOT NULL,
   [TransactionCurrencyId] uniqueidentifier NOT NULL,
   [PaymentTerms] nvarchar(2000) NOT NULL, [DueDays] int NOT NULL,
   [TaxNumber] nvarchar(64) NULL, [CountryId] uniqueidentifier NOT NULL,
   [BankBeneficiary] nvarchar(200) NULL, [Iban] nvarchar(64) NULL,
   [CreatedBy] nvarchar(200) NOT NULL, [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL, [LastModifiedAt] datetimeoffset(7) NOT NULL,
   [City] nvarchar(200) NOT NULL, [District] nvarchar(200) NULL,
   [Street] nvarchar(200) NOT NULL, [BuildingNumber] nvarchar(32) NULL,
   [PostalCode] nvarchar(32) NULL, [AdditionalNumber] nvarchar(32) NULL,
   CONSTRAINT [PK_Suppliers] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_Supplier_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_Supplier_ControlAccount] FOREIGN KEY([CompanyId],[ControlAccountId])
      REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [FK_Supplier_Currency] FOREIGN KEY([CompanyId],[TransactionCurrencyId])
      REFERENCES [Currencies]([CompanyId],[Id]),
   CONSTRAINT [CK_Supplier_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt]),
   CONSTRAINT [CK_Supplier_DueDays] CHECK ([DueDays]>=0),
   CONSTRAINT [CK_Supplier_Country] CHECK ([CountryId] <> '00000000-0000-0000-0000-000000000000')
  );
  CREATE UNIQUE INDEX [IX_Suppliers_CompanyId_Code] ON [Suppliers]([CompanyId],[Code]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(49,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
