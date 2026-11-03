-- SYS12-01: source fields only. Never render or activate unapproved templates.
-- Country-mandatory fields and draft watermarking await SYS12-02 policy.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=41)
     THROW 51001,'Apply schema 041 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=42)
     AND OBJECT_ID(N'dbo.PrintTemplates',N'U') IS NULL
     THROW 51002,'Schema 042 belongs to another module; rebase and renumber SYS12 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=42)
 BEGIN
  CREATE TABLE [PrintTemplates](
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL, [BranchId] uniqueidentifier NULL,
   [StatusCode] nvarchar(32) NOT NULL, [DocumentTypeCode] nvarchar(64) NOT NULL,
   [LanguageCode] nvarchar(32) NOT NULL, [PaperSizeCode] nvarchar(32) NOT NULL,
   [Name] nvarchar(200) NOT NULL, [VersionCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [IsDraft] bit NOT NULL, [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL, [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_PrintTemplates] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_PrintTemplate_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_PrintTemplate_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_PrintTemplate_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(42,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
