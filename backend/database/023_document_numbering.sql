-- SYS10 configurable non-tax numbering. Existing documents and legacy counters are preserved.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=22) THROW 51001,'Apply schema 022 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=23)
 BEGIN
  CREATE TABLE [DocumentNumberSeries](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,[DocumentKind] int NOT NULL,[FiscalYearId] uniqueidentifier NOT NULL,
   [Prefix] nvarchar(32) NULL,[NextNumber] bigint NOT NULL,[IsActive] bit NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,[CreatedAt] datetimeoffset NOT NULL,[Revision] int NOT NULL,[LastModifiedAt] datetimeoffset NOT NULL,
   CONSTRAINT [PK_DocumentNumberSeries] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_NumberSeries_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_NumberSeries_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_NumberSeries_Year] FOREIGN KEY([CompanyId],[FiscalYearId]) REFERENCES [FiscalYears]([CompanyId],[Id]),
   CONSTRAINT [CK_NumberSeries_Kind] CHECK([DocumentKind] IN (0,1)),
   CONSTRAINT [CK_NumberSeries_Next] CHECK([NextNumber]>0 AND [NextNumber]<9223372036854775807),
   CONSTRAINT [CK_NumberSeries_Revision] CHECK([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE UNIQUE INDEX [IX_DocumentNumberSeries_CompanyId_DocumentKind_FiscalYearId_BranchId] ON [DocumentNumberSeries]([CompanyId],[DocumentKind],[FiscalYearId],[BranchId]);
  CREATE TABLE [DocumentNumberAllocations](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,[Version] uniqueidentifier NOT NULL,
   [DocumentId] uniqueidentifier NULL,[JournalId] uniqueidentifier NULL,[InventoryOpeningId] uniqueidentifier NULL,[SeriesId] uniqueidentifier NOT NULL,[SeriesVersion] uniqueidentifier NOT NULL,[BranchId] uniqueidentifier NOT NULL,
   [DocumentKind] int NOT NULL,[Sequence] bigint NOT NULL,[Number] nvarchar(64) NOT NULL,[ReservedBy] nvarchar(200) NOT NULL,[ReservedAt] datetimeoffset NOT NULL,
   CONSTRAINT [PK_DocumentNumberAllocations] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_NumberAllocation_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_NumberAllocation_Series] FOREIGN KEY([CompanyId],[SeriesId]) REFERENCES [DocumentNumberSeries]([CompanyId],[Id]),
   CONSTRAINT [FK_NumberAllocation_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_NumberAllocation_Journal] FOREIGN KEY([CompanyId],[JournalId]) REFERENCES [Journals]([CompanyId],[Id]),
   CONSTRAINT [FK_NumberAllocation_Opening] FOREIGN KEY([CompanyId],[InventoryOpeningId]) REFERENCES [InventoryOpenings]([CompanyId],[Id]),
   CONSTRAINT [CK_NumberAllocation_Document] CHECK(([DocumentId] IS NULL AND [JournalId] IS NULL AND [InventoryOpeningId] IS NULL) OR ([DocumentKind]=0 AND [DocumentId] IS NOT NULL AND [JournalId] IS NOT NULL AND [DocumentId]=[JournalId] AND [InventoryOpeningId] IS NULL) OR ([DocumentKind]=1 AND [DocumentId] IS NOT NULL AND [InventoryOpeningId] IS NOT NULL AND [DocumentId]=[InventoryOpeningId] AND [JournalId] IS NULL)),
   CONSTRAINT [CK_NumberAllocation_Positive] CHECK([Sequence]>0 AND [DocumentKind] IN (0,1))
  );
  CREATE UNIQUE INDEX [IX_DocumentNumberAllocations_CompanyId_DocumentKind_Number] ON [DocumentNumberAllocations]([CompanyId],[DocumentKind],[Number]);
  CREATE UNIQUE INDEX [IX_DocumentNumberAllocations_CompanyId_SeriesId_Sequence] ON [DocumentNumberAllocations]([CompanyId],[SeriesId],[Sequence]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(23,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
