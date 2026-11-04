-- SYS06-FG05 partial: technical audit metadata for ERP membership grant/status.
-- The external identity profile, email, language and MFA remain IdP-owned.
-- Existing users retain unknown creation provenance; do not backfill actor/time.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
     @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=46)
     THROW 51001,'Apply schema 046 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=47)
    AND (COL_LENGTH(N'dbo.Memberships',N'CreatedBy') IS NULL
      OR COL_LENGTH(N'dbo.Memberships',N'CreatedAt') IS NULL
      OR COL_LENGTH(N'dbo.Memberships',N'Revision') IS NULL
      OR COL_LENGTH(N'dbo.Memberships',N'LastModifiedAt') IS NULL
      OR NOT EXISTS (SELECT 1 FROM sys.check_constraints
          WHERE parent_object_id=OBJECT_ID(N'dbo.Memberships')
          AND name=N'CK_Membership_AuditMetadata'))
     THROW 51002,'Schema 047 owned by another module; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=47)
 BEGIN
    ALTER TABLE dbo.Memberships ADD
        [CreatedBy] nvarchar(200) NULL,
        [CreatedAt] datetimeoffset(7) NULL,
        [Revision] int NULL,
        [LastModifiedAt] datetimeoffset(7) NULL;
    -- Compile after ALTER ADD, within the same schema-locked transaction.
    EXEC sys.sp_executesql N'ALTER TABLE dbo.Memberships WITH CHECK ADD
       CONSTRAINT CK_Membership_AuditMetadata CHECK
       (([CreatedBy] IS NULL AND [CreatedAt] IS NULL AND
         [Revision] IS NULL AND [LastModifiedAt] IS NULL)
        OR ([CreatedBy] IS NOT NULL AND LEN(LTRIM(RTRIM([CreatedBy])))>0
         AND [CreatedAt] IS NOT NULL AND [Revision]>=1 AND
         [LastModifiedAt]>=[CreatedAt]))';
    INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
        VALUES(47,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
