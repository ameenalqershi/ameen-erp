-- SYS08 user-role association prerequisite for SYS07; no grants or assignments seeded.
-- Application write guard remains active pending policy, audit and API acceptance.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=29) THROW 51001,'Apply schema 029 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=30)
 BEGIN
  CREATE TABLE [SecurityRoleAssignments](
   [CompanyId] uniqueidentifier NOT NULL,[Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,[MembershipId] uniqueidentifier NOT NULL,
   [RoleId] uniqueidentifier NOT NULL,
   CONSTRAINT [PK_SecurityRoleAssignments] PRIMARY KEY([CompanyId],[Id]),
   CONSTRAINT [FK_SecurityRoleAssignment_Company] FOREIGN KEY([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_SecurityRoleAssignment_Membership] FOREIGN KEY([CompanyId],[MembershipId]) REFERENCES [Memberships]([CompanyId],[Id]),
   CONSTRAINT [FK_SecurityRoleAssignment_Role] FOREIGN KEY([CompanyId],[RoleId]) REFERENCES [SecurityRoles]([CompanyId],[Id])
  );
  CREATE UNIQUE INDEX [IX_SecurityRoleAssignments_CompanyId_MembershipId_RoleId]
   ON [SecurityRoleAssignments]([CompanyId],[MembershipId],[RoleId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(30,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
