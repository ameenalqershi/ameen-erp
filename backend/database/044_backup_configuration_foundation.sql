-- SYS15-01 schema only. No backup/restore, scheduler, destination access
-- or encryption operation is authorized by creating this table.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=43)
    THROW 51001,'Apply schema 043 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=44)
     AND OBJECT_ID(N'dbo.BackupJobConfigurations',N'U') IS NULL
    THROW 51002,'Schema 044 belongs to another module; rebase and renumber SYS15 migration',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=44)
 BEGIN
  CREATE TABLE [BackupJobConfigurations] (
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL, [BranchId] uniqueidentifier NULL,
   [StatusCode] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL,
   [ScheduleText] nvarchar(200) NOT NULL, [Destination] nvarchar(2000) NOT NULL,
   [RetentionPolicy] int NOT NULL, [IsEncrypted] bit NOT NULL,
   [LastTestReference] nvarchar(200) NULL, [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL, [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_BackupJobConfigurations] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_BackupJob_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_BackupJob_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_BackupJob_Retention] CHECK ([RetentionPolicy]>=0),
   CONSTRAINT [CK_BackupJob_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(44,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
