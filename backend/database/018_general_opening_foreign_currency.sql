-- Ameen ERP schema 018: permit dated foreign currency opening snapshots, preserving journal history.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=17) THROW 51001, 'Apply schema 017 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=18)
    BEGIN
        ALTER TABLE [Journals] DROP CONSTRAINT [CK_Journal_GeneralOpening];
        ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_GeneralOpening] CHECK (
            ([Source] = 4 AND [OperationStartsOn] IS NOT NULL
             AND [PreviousTrialBalanceReference] IS NOT NULL AND LEN([PreviousTrialBalanceReference]) > 0
             AND [OpeningCurrencyCode] IS NOT NULL
             AND [OpeningExchangeRate] > 0 AND [OpeningExchangeRate] < 1000000000
             AND [OpeningExchangeRate] IS NOT NULL
             AND [OpeningRevision] > 0 AND [OpeningRevision] IS NOT NULL AND [OpeningModifiedAt] IS NOT NULL)
            OR ([Source] <> 4 AND [OperationStartsOn] IS NULL
                AND [PreviousTrialBalanceReference] IS NULL AND [OpeningCurrencyCode] IS NULL
                AND [OpeningExchangeRate] IS NULL AND [OpeningRevision] IS NULL AND [OpeningModifiedAt] IS NULL)
        );
        INSERT INTO dbo.__AmeenSchemaVersions([Version], AppliedAt) VALUES(18, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
