-- GL05/GL06 durable processing envelope, without rewriting prior financial entries.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=24) THROW 51001,'Apply schema 024 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=25)
 BEGIN
  CREATE TABLE [JournalProcessingBatches](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[BatchKey] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Action] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [ActorId] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [RequestHash] nvarchar(64) NOT NULL,[ItemCount] int NOT NULL,
   [Revision] int NOT NULL,[CreatedAt] datetimeoffset NOT NULL,
   [LastModifiedAt] datetimeoffset NOT NULL,[FinishedAt] datetimeoffset NULL,
   [RowsJson] nvarchar(max) NOT NULL,
   CONSTRAINT [PK_JournalProcessingBatches] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_ProcessingBatch_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [CK_ProcessingBatch_Count] CHECK([ItemCount] BETWEEN 1 AND 100),
   CONSTRAINT [CK_ProcessingBatch_Revision] CHECK([Revision]>=1 AND [LastModifiedAt]>=[CreatedAt] AND ([FinishedAt] IS NULL OR [FinishedAt]>=[CreatedAt]))
  );
  CREATE UNIQUE INDEX [IX_JournalProcessingBatches_CompanyId_BatchKey] ON [JournalProcessingBatches]([CompanyId],[BatchKey]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(25,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
