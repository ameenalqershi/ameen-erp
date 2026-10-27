-- Ameen ERP schema 022: GL03-F27/F28 numeric revision and server modification timestamp.
-- Preserve UUID concurrency, historical amounts, document dates and source links.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=21)
        THROW 51001, 'Apply schema 021 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=22)
    BEGIN
        ALTER TABLE [Journals] ADD
            [Revision] int NOT NULL CONSTRAINT [DF_Journals_Revision] DEFAULT (1),
            [LastModifiedAt] datetimeoffset NULL;
        -- Defer compilation until the columns exist; SQL Server otherwise compiles the whole batch first.
        EXEC sys.sp_executesql N'
            ;WITH changes AS (
                SELECT a.[CompanyId], a.[ResourceId], a.[OccurredAt]
                FROM [AuditEvents] a INNER JOIN [Journals] j ON j.[CompanyId]=a.[CompanyId] AND j.[Id]=a.[ResourceId]
                WHERE a.[Action] IN (''journal.edited'',''general-opening.edited'',''journal.submit'',
                    ''journal.approve'',''journal.return'',''journal.cancel'',''journal.reversed'')
                    OR (a.[Action]=''journal.post'' AND j.[Source]<>3)
                UNION ALL
                -- A reversal reservation/release is audited on its child document, including older history.
                -- Do not also count general-opening.reversal-* events for the same mutation.
                SELECT child.[CompanyId], child.[ReversesJournalId], a.[OccurredAt]
                FROM [AuditEvents] a INNER JOIN [Journals] child ON child.[CompanyId]=a.[CompanyId] AND child.[Id]=a.[ResourceId]
                WHERE child.[Source]=2 AND child.[ReversesJournalId] IS NOT NULL
                    AND a.[Action] IN (''journal.reversal-drafted'',''journal.cancel'')
            ), history AS (
                SELECT [CompanyId], [ResourceId], COUNT_BIG(*) AS [ChangeCount], MAX([OccurredAt]) AS LastChange
                FROM changes GROUP BY [CompanyId], [ResourceId]
            )
            UPDATE j SET j.[Revision]=1+CAST(COALESCE(h.[ChangeCount],0) AS int),
                j.[LastModifiedAt]=(SELECT MAX(v.[OccurredAt]) FROM (VALUES (j.[CreatedAt]),(j.[PostedAt]),(h.LastChange)) v([OccurredAt]))
            FROM [Journals] j LEFT JOIN history h ON h.[CompanyId]=j.[CompanyId] AND h.[ResourceId]=j.[Id];
            ALTER TABLE [Journals] ALTER COLUMN [LastModifiedAt] datetimeoffset NOT NULL;
            ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_Revision]
                CHECK ([Revision]>0 AND [LastModifiedAt]>=[CreatedAt]);';
        INSERT INTO dbo.__AmeenSchemaVersions ([Version],[AppliedAt]) VALUES (22,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
