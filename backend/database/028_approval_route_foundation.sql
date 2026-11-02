-- SYS09 foundation for GL14 dependency: no routes, roles or policies are seeded.
-- Routes remain unwritable via EF until SYS07 role validation and SYS09 conflict
-- resolution, scope and approval policies are implemented.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=27) THROW 51001,'Apply schema 027 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=28)
 BEGIN
  CREATE TABLE [ApprovalRoutes](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[BranchId] uniqueidentifier NULL,
   [IsActive] bit NOT NULL,[DocumentType] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [MinimumAmount] decimal(20,6) NOT NULL,[MaximumAmount] decimal(20,6) NULL,
   [CurrencyId] uniqueidentifier NOT NULL,[SeparateCreatorAndApprover] bit NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,[CreatedAt] datetimeoffset NOT NULL,
   [Revision] int NOT NULL,[LastModifiedAt] datetimeoffset NOT NULL,
   CONSTRAINT [PK_ApprovalRoutes] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_ApprovalRoute_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_ApprovalRoute_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_ApprovalRoute_Currency] FOREIGN KEY([CompanyId],[CurrencyId]) REFERENCES [Currencies]([CompanyId],[Id]),
   CONSTRAINT [CK_ApprovalRoute_Amount] CHECK([MinimumAmount]>=0 AND ([MaximumAmount] IS NULL OR [MaximumAmount]>=[MinimumAmount]),
   CONSTRAINT [CK_ApprovalRoute_Revision] CHECK([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE TABLE [ApprovalRouteStages](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[ApprovalRouteId] uniqueidentifier NOT NULL,
   [StageNumber] int NOT NULL,[ApproverRoleId] uniqueidentifier NOT NULL,
   [AlertAfterMinutes] int NULL,
   CONSTRAINT [PK_ApprovalRouteStages] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_ApprovalRouteStage_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_ApprovalRouteStage_Route] FOREIGN KEY([CompanyId],[ApprovalRouteId]) REFERENCES [ApprovalRoutes]([CompanyId],[Id]) ON DELETE CASCADE,
   CONSTRAINT [CK_ApprovalRouteStage_Number] CHECK([StageNumber]>0 AND ([AlertAfterMinutes] IS NULL OR [AlertAfterMinutes]>=0])
  );
  CREATE UNIQUE INDEX [IX_ApprovalRouteStages_CompanyId_ApprovalRouteId_StageNumber]
   ON [ApprovalRouteStages]([CompanyId],[ApprovalRouteId],[StageNumber]);
  ALTER TABLE [JournalDocumentTypes] ADD CONSTRAINT [FK_JournalDocumentType_ApprovalRoute]
   FOREIGN KEY([CompanyId],[ApprovalRouteId]) REFERENCES [ApprovalRoutes]([CompanyId],[Id]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(28,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
