-- INV16-01: field-only, fail-closed movement reason foundation.
-- Direction and status values, cost-center/counter-account conditions,
-- document approval and source routing remain pending explicit policy.
-- No operational movement or approval may rely on this table yet.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=48)
  THROW 51001,'Apply schema 048 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
   AND OBJECT_ID(N'dbo.InventoryMovementReasons',N'U') IS NULL
  THROW 51002,'Schema 049 belongs to another module; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=49)
 BEGIN
  CREATE TABLE [InventoryMovementReasons](
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL, [BranchId] uniqueidentifier NULL,
   [StatusCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [ReasonCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Description] nvarchar(200) NOT NULL,
   [DirectionCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [CounterAccountId] uniqueidentifier NULL,
   [RequireCostCenter] bit NOT NULL, [ApprovalRouteId] uniqueidentifier NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL, [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL, [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_InventoryMovementReasons] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryMovementReason_Company] FOREIGN KEY([CompanyId])
    REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_InventoryMovementReason_Branch] FOREIGN KEY([CompanyId],[BranchId])
    REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryMovementReason_Account] FOREIGN KEY([CompanyId],[CounterAccountId])
    REFERENCES [Accounts]([CompanyId],[Id]),
   CONSTRAINT [FK_InventoryMovementReason_ApprovalRoute] FOREIGN KEY([CompanyId],[ApprovalRouteId])
    REFERENCES [ApprovalRoutes]([CompanyId],[Id]),
   CONSTRAINT [CK_InventoryMovementReason_Audit] CHECK
    ([Revision]>=1 AND [LastModifiedAt]>=[CreatedAt] AND LEN(LTRIM(RTRIM([CreatedBy])))>0),
   CONSTRAINT [CK_InventoryMovementReason_Fields] CHECK
    (LEN(LTRIM(RTRIM([StatusCode])))>0 AND LEN(LTRIM(RTRIM([ReasonCode])))>0
      AND LEN(LTRIM(RTRIM([DirectionCode])))>0 AND LEN(LTRIM(RTRIM([Description])))>0),
   CONSTRAINT [CK_InventoryMovementReason_References] CHECK
    ([ApprovalRouteId] <> '00000000-0000-0000-0000-000000000000'
      AND ([BranchId] IS NULL OR [BranchId] <> '00000000-0000-0000-0000-000000000000')
      AND ([CounterAccountId] IS NULL OR [CounterAccountId] <> '00000000-0000-0000-0000-000000000000'))
  );
  -- No uniqueness assumption across branch/company until owner approves it.
  CREATE INDEX [IX_InventoryMovementReasons_Company_ReasonCode]
   ON [InventoryMovementReasons]([CompanyId],[ReasonCode]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
   VALUES(49,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
