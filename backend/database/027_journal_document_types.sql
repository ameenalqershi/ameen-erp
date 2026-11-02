-- GL14-01: structural foundation only. SYS09 approval routes are not yet implemented.
-- No document type is seeded or activated; this migration does not change posting behavior.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=26) THROW 51001,'Apply schema 026 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=27)
 BEGIN
  CREATE TABLE [JournalDocumentTypes](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[BranchId] uniqueidentifier NULL,
   [IsActive] bit NOT NULL,[Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Name] nvarchar(200) NOT NULL,[Origin] int NOT NULL,
   [NumberSeriesId] uniqueidentifier NOT NULL,[ApprovalRouteId] uniqueidentifier NOT NULL,
   [RequiresAttachment] bit NOT NULL,[CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset NOT NULL,[Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset NOT NULL,
   CONSTRAINT [PK_JournalDocumentTypes] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_JournalDocumentType_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_JournalDocumentType_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_JournalDocumentType_NumberSeries] FOREIGN KEY([CompanyId],[NumberSeriesId]) REFERENCES [DocumentNumberSeries]([CompanyId],[Id]),
   CONSTRAINT [CK_JournalDocumentType_Origin] CHECK([Origin] IN (0,1)),
   CONSTRAINT [CK_JournalDocumentType_Revision] CHECK([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE UNIQUE INDEX [IX_JournalDocumentTypes_CompanyId_Code] ON [JournalDocumentTypes]([CompanyId],[Code]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(27,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
