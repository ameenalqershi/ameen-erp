-- SYS03-FG04: record real provenance for newly created fiscal years.
-- Legacy years retain NULL metadata rather than fabricated actor/date/revision.
-- This is not fiscal calendar edit or full-year close implementation.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
     @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=45)
     THROW 51001,'Apply schema 045 first',1;
 IF EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=46)
    AND (COL_LENGTH(N'dbo.FiscalYears',N'CreatedBy') IS NULL
      OR COL_LENGTH(N'dbo.FiscalYears',N'CreatedAt') IS NULL
      OR COL_LENGTH(N'dbo.FiscalYears',N'Revision') IS NULL
      OR COL_LENGTH(N'dbo.FiscalYears',N'LastModifiedAt') IS NULL
      OR NOT EXISTS (SELECT 1 FROM sys.check_constraints
          WHERE parent_object_id=OBJECT_ID(N'dbo.FiscalYears')
          AND name=N'CK_FiscalYear_AuditMetadata'))
     THROW 51002,'Schema 046 owned by another module; rebase and renumber',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=46)
 BEGIN
    ALTER TABLE dbo.FiscalYears ADD
        [CreatedBy] nvarchar(200) NULL,
        [CreatedAt] datetimeoffset(7) NULL,
        [Revision] int NULL,
        [LastModifiedAt] datetimeoffset(7) NULL;
    -- SQL Server compiles a batch before the ALTER ADD columns exist.
    -- Compile the new-column CHECK only after the preceding DDL executes;
    -- sp_executesql retains this same schema-lock transaction and rollback.
    EXEC sys.sp_executesql N'ALTER TABLE dbo.FiscalYears WITH CHECK ADD
        CONSTRAINT CK_FiscalYear_AuditMetadata CHECK
        (([CreatedBy] IS NULL AND [CreatedAt] IS NULL AND
          [Revision] IS NULL AND [LastModifiedAt] IS NULL)
         OR ([CreatedBy] IS NOT NULL AND LEN(LTRIM(RTRIM([CreatedBy])))>0
             AND [CreatedAt] IS NOT NULL AND [Revision]>=1 AND
             [LastModifiedAt]>=[CreatedAt]))';
    INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
       VALUES(46,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
