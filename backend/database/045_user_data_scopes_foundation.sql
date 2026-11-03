-- SYS08: user/role scope metadata only; no authorization grants activated.
-- Account/customer group catalogues are absent. Their placeholder GUIDs
-- must remain NULL until separate, tenant-safe references are approved.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @result int;
 EXEC @result=sys.sp_getapplock @Resource=N'ameen:schema',
    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @result<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=44)
    THROW 51001,'Apply schema 044 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=45)
   AND (OBJECT_ID(N'dbo.UserDataScopes',N'U') IS NULL
      OR OBJECT_ID(N'dbo.UserDataScopeLines',N'U') IS NULL)
    THROW 51002,'Schema 045 belongs to another module; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=45)
 BEGIN
  CREATE TABLE [UserDataScopes](
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL, [BranchId] uniqueidentifier NULL,
   [StatusCode] nvarchar(32) NOT NULL, [MembershipId] uniqueidentifier NOT NULL,
   [RoleId] uniqueidentifier NOT NULL, [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL, [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_UserDataScopes] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScope_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_UserDataScope_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScope_Member] FOREIGN KEY([CompanyId],[MembershipId]) REFERENCES [Memberships]([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScope_Role] FOREIGN KEY([CompanyId],[RoleId]) REFERENCES [SecurityRoles]([CompanyId],[Id]),
   CONSTRAINT [CK_UserDataScope_Audit] CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt])
  );
  CREATE TABLE [UserDataScopeLines](
   [CompanyId] uniqueidentifier NOT NULL, [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL, [ScopeId] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NOT NULL, [WarehouseId] uniqueidentifier NULL,
   [CashBoxId] uniqueidentifier NULL, [AccountGroupId] uniqueidentifier NULL,
   [CustomerGroupId] uniqueidentifier NULL,
   CONSTRAINT [PK_UserDataScopeLines] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScopeLine_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_UserDataScopeLine_Scope] FOREIGN KEY([CompanyId],[ScopeId]) REFERENCES [UserDataScopes]([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScopeLine_Branch] FOREIGN KEY([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScopeLine_Warehouse] FOREIGN KEY([CompanyId],[WarehouseId]) REFERENCES [Warehouses]([CompanyId],[Id]),
   CONSTRAINT [FK_UserDataScopeLine_CashBox] FOREIGN KEY([CompanyId],[CashBoxId]) REFERENCES [CashBoxes]([CompanyId],[Id]),
   CONSTRAINT [CK_UserDataScopeLine_UnapprovedGroups] CHECK
      ([AccountGroupId] IS NULL AND [CustomerGroupId] IS NULL)
  );
  CREATE INDEX [IX_UserDataScopeLines_Scope] ON [UserDataScopeLines]([CompanyId],[ScopeId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
     VALUES(45,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
