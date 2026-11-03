-- SYS13-01 metadata foundation. No import, preview, approval or GL operation
-- starts from creation of these tables. Source/duplicate codes are unapproved.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=42)
     THROW 51001,'Apply schema 042 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=43)
    AND (OBJECT_ID(N'dbo.DataImportBatches',N'U') IS NULL
      OR OBJECT_ID(N'dbo.DataImportFieldMappings',N'U') IS NULL)
     THROW 51002,'Schema 043 belongs to another module; rebase and renumber SYS13 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=43)
 BEGIN
  CREATE TABLE [DataImportBatches] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL, [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL, [StatusCode] nvarchar(32) NOT NULL,
   [DataKindCode] nvarchar(64) NOT NULL, [FileIdentifier] nvarchar(200) NOT NULL,
   [CutoffDate] date NOT NULL, [DuplicatePolicyCode] nvarchar(64) NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL, [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL, [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_DataImportBatches] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_DataImport_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_DataImport_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_DataImport_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE TABLE [DataImportFieldMappings] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL, [BatchId] uniqueidentifier NOT NULL,
   [SourceColumn] nvarchar(200) NOT NULL, [TargetField] nvarchar(200) NOT NULL,
   [DefaultValue] nvarchar(2000) NULL,
   CONSTRAINT [PK_DataImportFieldMappings] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_DataImportMap_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_DataImportMap_Batch] FOREIGN KEY ([CompanyId],[BatchId])
     REFERENCES [DataImportBatches]([CompanyId],[Id])
  );
  CREATE INDEX [IX_DataImportFieldMappings_Batch]
    ON [DataImportFieldMappings]([CompanyId],[BatchId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(43,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
