-- SYS07 role catalog foundation for SYS09; no role, grant or policy is seeded.
-- EF blocks all role writes until membership, deny precedence and scope enforcement
-- have positive and negative acceptance. The SYS09 write guard remains enabled.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=28) THROW 51001,'Apply schema 028 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=29)
 BEGIN
  CREATE TABLE [SecurityRoles](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[BranchId] uniqueidentifier NULL,
   [IsActive] bit NOT NULL,[Name] nvarchar(200) NOT NULL,
   [Responsibility] nvarchar(500) NOT NULL,[CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset NOT NULL,[Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset NOT NULL,
   CONSTRAINT [PK_SecurityRoles] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_SecurityRole_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_SecurityRole_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [CK_SecurityRole_Revision] CHECK([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE TABLE [SecurityRoleRules](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[RoleId] uniqueidentifier NOT NULL,
   [ScreenCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [OperationCode] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   [Allowed] bit NOT NULL,[AmountLimit] decimal(20,6) NULL,[CurrencyId] uniqueidentifier NULL,
   CONSTRAINT [PK_SecurityRoleRules] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_SecurityRoleRule_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_SecurityRoleRule_Role] FOREIGN KEY([CompanyId],[RoleId]) REFERENCES [SecurityRoles]([CompanyId],[Id]) ON DELETE CASCADE,
   CONSTRAINT [FK_SecurityRoleRule_Currency] FOREIGN KEY([CompanyId],[CurrencyId]) REFERENCES [Currencies]([CompanyId],[Id]),
   CONSTRAINT [CK_SecurityRoleRule_Limit] CHECK(
    ([AmountLimit] IS NULL AND [CurrencyId] IS NULL) OR
    ([Allowed]=1 AND [AmountLimit]>=0 AND [CurrencyId] IS NOT NULL))
  );
  CREATE UNIQUE INDEX [IX_SecurityRoleRules_CompanyId_RoleId_ScreenCode_OperationCode_CurrencyId]
   ON [SecurityRoleRules]([CompanyId],[RoleId],[ScreenCode],[OperationCode],[CurrencyId]);
  ALTER TABLE [ApprovalRouteStages] ADD CONSTRAINT [FK_ApprovalRouteStage_ApproverRole]
   FOREIGN KEY([CompanyId],[ApproverRoleId]) REFERENCES [SecurityRoles]([CompanyId],[Id]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(29,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
