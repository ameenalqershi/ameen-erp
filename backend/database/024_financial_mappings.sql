-- GL13 auditable financial maps. No policy or account assignments are inferred.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=23) THROW 51001,'Apply schema 023 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=24)
 BEGIN
  CREATE TABLE [FinancialMappings](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,[Name] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [StatementKind] int NOT NULL,[VersionCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [EffectiveFrom] date NOT NULL,[IsActive] bit NOT NULL,[CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset NOT NULL,[Revision] int NOT NULL,[LastModifiedAt] datetimeoffset NOT NULL,
   CONSTRAINT [PK_FinancialMappings] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_FinancialMapping_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_FinancialMapping_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_FinancialMapping_Kind] CHECK([StatementKind] BETWEEN 0 AND 3),
   CONSTRAINT [CK_FinancialMapping_Revision] CHECK([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE UNIQUE INDEX [IX_FinancialMappings_CompanyId_BranchId_StatementKind_Name_VersionCode]
   ON [FinancialMappings]([CompanyId],[BranchId],[StatementKind],[Name],[VersionCode]);
  CREATE TABLE [FinancialMappingLines](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [MappingId] uniqueidentifier NOT NULL,[Position] int NOT NULL,[Code] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [ParentCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL,[ParentLineId] uniqueidentifier NULL,
   [Title] nvarchar(200) NOT NULL,[AccountId] uniqueidentifier NULL,[DisplaySign] int NOT NULL,[Formula] nvarchar(2000) NULL,
   CONSTRAINT [PK_FinancialMappingLines] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [AK_FinancialMappingLines_CompanyId_MappingId_Id] UNIQUE([CompanyId],[MappingId],[Id]),
   CONSTRAINT [FK_FinancialMappingLine_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_FinancialMappingLine_Map] FOREIGN KEY([CompanyId],[MappingId]) REFERENCES [FinancialMappings]([CompanyId],[Id]) ON DELETE CASCADE,
   CONSTRAINT [FK_FinancialMappingLine_Account] FOREIGN KEY([CompanyId],[AccountId]) REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [FK_FinancialMappingLine_Parent] FOREIGN KEY([CompanyId],[MappingId],[ParentLineId]) REFERENCES [FinancialMappingLines]([CompanyId],[MappingId],[Id]),
   CONSTRAINT [CK_FinancialMappingLine_Kind] CHECK([DisplaySign] IN (-1,1) AND [Position] BETWEEN 1 AND 500 AND ([AccountId] IS NULL OR [Formula] IS NULL)),
   CONSTRAINT [CK_FinancialMappingLine_Parent] CHECK(([ParentCode] IS NULL AND [ParentLineId] IS NULL) OR ([ParentCode] IS NOT NULL AND [ParentLineId] IS NOT NULL AND [ParentLineId]<>[Id]))
  );
  CREATE UNIQUE INDEX [IX_FinancialMappingLines_CompanyId_MappingId_Code] ON [FinancialMappingLines]([CompanyId],[MappingId],[Code]);
  CREATE UNIQUE INDEX [IX_FinancialMappingLines_CompanyId_MappingId_Position] ON [FinancialMappingLines]([CompanyId],[MappingId],[Position]);
  CREATE TABLE [FinancialMappingReviews](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [MappingId] uniqueidentifier NOT NULL,[MappingVersion] uniqueidentifier NOT NULL,[AsOf] date NOT NULL,
   [ReviewedBy] nvarchar(200) NOT NULL,[ReviewedAt] datetimeoffset NOT NULL,[Ready] bit NOT NULL,[SnapshotJson] nvarchar(max) NOT NULL,
   CONSTRAINT [PK_FinancialMappingReviews] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_FinancialMappingReview_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_FinancialMappingReview_Map] FOREIGN KEY([CompanyId],[MappingId]) REFERENCES [FinancialMappings]([CompanyId],[Id])
  );
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(24,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
