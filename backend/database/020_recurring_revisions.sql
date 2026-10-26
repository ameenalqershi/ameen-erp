-- Ameen ERP schema 020: separate numeric revision and last modification for GL04 templates.
-- Preserve historical source amounts, linked generated journals, UUID concurrency and audit records.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=19) THROW 51001, 'Apply schema 019 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=20)
    BEGIN
        ALTER TABLE [RecurringJournalTemplates] ADD
            [Revision] int NOT NULL CONSTRAINT [DF_RecurringJournalTemplates_Revision] DEFAULT (1),
            [LastModifiedAt] datetimeoffset NULL;
        -- SQL Server compiles this migration batch before new columns exist.
        -- Deferred compilation preserves atomic DDL and historical backfill in the transaction.
        EXEC sys.sp_executesql N'
            ;WITH history AS (
                SELECT [CompanyId], [ResourceId], COUNT_BIG(*) AS RevisionCount, MAX([OccurredAt]) AS LastChange
                FROM [AuditEvents]
                WHERE [Action] IN (''recurring.edited'', ''recurring.status-changed'')
                GROUP BY [CompanyId], [ResourceId]
            )
            UPDATE t SET
                t.[Revision] = 1 + CAST(COALESCE(h.RevisionCount, 0) AS int),
                t.[LastModifiedAt] = CASE WHEN h.LastChange > t.[CreatedAt] THEN h.LastChange ELSE t.[CreatedAt] END
            FROM [RecurringJournalTemplates] t LEFT JOIN history h
                ON h.CompanyId = t.CompanyId AND h.ResourceId = t.Id;';
        ALTER TABLE [RecurringJournalTemplates] ALTER COLUMN [LastModifiedAt] datetimeoffset NOT NULL;
        ALTER TABLE [RecurringJournalTemplates] ADD CONSTRAINT [CK_Recurring_Revision]
            CHECK ([Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]);
        INSERT INTO dbo.__AmeenSchemaVersions ([Version],[AppliedAt]) VALUES (20,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
