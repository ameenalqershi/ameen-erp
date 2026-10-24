SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=16) THROW 51001, 'Apply schema 016 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=17)
    BEGIN
        EXEC(N'ALTER TABLE [Journals] ADD [OperationStartsOn] date NULL, [PreviousTrialBalanceReference] nvarchar(500) NULL, [OpeningCurrencyCode] nvarchar(3) NULL, [OpeningExchangeRate] decimal(20,10) NULL, [OpeningRevision] int NULL, [OpeningModifiedAt] datetimeoffset NULL;');
        EXEC(N'ALTER TABLE [Journals] DROP CONSTRAINT [CK_Journal_Source];');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_Source] CHECK (([Source] IN (0, 4) AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 1 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NOT NULL AND [SourceTemplateVersion] IS NOT NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 2 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NOT NULL) OR ([Source] = 3 AND [InventoryOpeningId] IS NOT NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL));');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_GeneralOpening] CHECK (([Source] = 4 AND [OperationStartsOn] IS NOT NULL AND [PreviousTrialBalanceReference] IS NOT NULL AND LEN([PreviousTrialBalanceReference]) > 0 AND [OpeningCurrencyCode] IS NOT NULL AND [OpeningExchangeRate] = 1 AND [OpeningExchangeRate] IS NOT NULL AND [OpeningRevision] > 0 AND [OpeningRevision] IS NOT NULL AND [OpeningModifiedAt] IS NOT NULL) OR ([Source] <> 4 AND [OperationStartsOn] IS NULL AND [PreviousTrialBalanceReference] IS NULL AND [OpeningCurrencyCode] IS NULL AND [OpeningExchangeRate] IS NULL AND [OpeningRevision] IS NULL AND [OpeningModifiedAt] IS NULL));');
        INSERT INTO dbo.__AmeenSchemaVersions([Version],AppliedAt) VALUES(17,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
