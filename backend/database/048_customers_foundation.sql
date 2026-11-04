-- SAL01-01: customer-card field foundation only; no operational customer creation
-- or credit/tax rule. SAL15 category, SAL09 salesperson, country identity policy
-- and authorization are pending; placeholders are NOT foreign keys.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @result int;
 EXEC @result=sys.sp_getapplock @Resource=N'ameen:schema',
    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @result<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=47)
    THROW 51001,'Apply schema 047 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=48)
   AND OBJECT_ID(N'dbo.Customers',N'U') IS NULL
    THROW 51002,'Schema 048 belongs to another module; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=48)
 BEGIN
  CREATE TABLE [Customers](
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Name] nvarchar(200) NOT NULL, [EnglishName] nvarchar(200) NULL,
   [IsActive] bit NOT NULL, [Notes] nvarchar(2000) NULL,
   [LegalName] nvarchar(200) NOT NULL,
   [CustomerCategoryId] uniqueidentifier NOT NULL,
   [ControlAccountId] uniqueidentifier NOT NULL,
   [TransactionCurrencyId] uniqueidentifier NOT NULL,
   [CreditLimit] decimal(20,6) NOT NULL, [CreditDays] int NOT NULL,
   [SalespersonId] uniqueidentifier NULL, [Phone] nvarchar(100) NULL,
   [CountryId] uniqueidentifier NOT NULL,
   [IdentityType] nvarchar(32) NULL, [TaxNumber] nvarchar(64) NULL,
   [City] nvarchar(200) NULL, [District] nvarchar(200) NULL,
   [Street] nvarchar(200) NULL, [BuildingNumber] nvarchar(32) NULL,
   [PostalCode] nvarchar(32) NULL, [AdditionalNumber] nvarchar(32) NULL,
   [CreatedBy] nvarchar(200) NOT NULL, [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL, [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_Customers] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_Customer_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_Customer_ControlAccount] FOREIGN KEY([CompanyId],[ControlAccountId])
      REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [FK_Customer_Currency] FOREIGN KEY([CompanyId],[TransactionCurrencyId])
      REFERENCES [Currencies]([CompanyId],[Id]),
   CONSTRAINT [CK_Customer_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt]),
   CONSTRAINT [CK_Customer_Credit] CHECK ([CreditLimit]>=0 AND [CreditDays]>=0),
   CONSTRAINT [CK_Customer_References] CHECK (
      [CustomerCategoryId] <> '00000000-0000-0000-0000-000000000000' AND
      [CountryId] <> '00000000-0000-0000-0000-000000000000' AND
      ([SalespersonId] IS NULL OR [SalespersonId] <> '00000000-0000-0000-0000-000000000000'))
  );
  CREATE UNIQUE INDEX [IX_Customers_CompanyId_Code] ON [Customers]([CompanyId],[Code]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(48,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
