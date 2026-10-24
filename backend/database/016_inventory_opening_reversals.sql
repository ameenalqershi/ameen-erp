SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=15) THROW 51001, 'Apply schema 015 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=16)
    BEGIN
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD [ReversesOpeningId] uniqueidentifier NULL, [ReversalReason] nvarchar(500) NULL;');
        EXEC(N'ALTER TABLE [StockMovements] ADD [ReversesMovementId] uniqueidentifier NULL;');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [CK_InventoryOpening_Reversal] CHECK (([ReversesOpeningId] IS NULL AND [ReversalReason] IS NULL) OR ([ReversesOpeningId] IS NOT NULL AND [ReversesOpeningId] <> [Id] AND [ReversalReason] IS NOT NULL));');
        EXEC(N'ALTER TABLE [StockMovements] DROP CONSTRAINT [CK_StockMovement_Amounts];');
        EXEC(N'ALTER TABLE [StockMovements] ADD CONSTRAINT [CK_StockMovement_Amounts] CHECK (([ReversesMovementId] IS NULL AND [BaseQuantity] > 0 AND [Value] >= 0) OR ([ReversesMovementId] IS NOT NULL AND [BaseQuantity] < 0 AND [Value] <= 0));');
        EXEC(N'ALTER TABLE [StockMovements] DROP CONSTRAINT [CK_StockMovement_Serial];');
        EXEC(N'ALTER TABLE [StockMovements] ADD CONSTRAINT [CK_StockMovement_Serial] CHECK ([IsSerial] = 0 OR ([IdentityId] IS NOT NULL AND ABS([BaseQuantity]) = 1));');
        EXEC(N'ALTER TABLE [Journals] DROP CONSTRAINT [CK_Journal_Source];');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_Source] CHECK (([Source] = 0 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 1 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NOT NULL AND [SourceTemplateVersion] IS NOT NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 2 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NOT NULL) OR ([Source] = 3 AND [InventoryOpeningId] IS NOT NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL));');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [FK_InventoryOpenings_InventoryOpenings_CompanyId_ReversesOpeningId] FOREIGN KEY ([CompanyId],[ReversesOpeningId]) REFERENCES [InventoryOpenings]([CompanyId],[Id]);');
        EXEC(N'ALTER TABLE [StockMovements] ADD CONSTRAINT [FK_StockMovements_StockMovements_CompanyId_ReversesMovementId] FOREIGN KEY ([CompanyId],[ReversesMovementId]) REFERENCES [StockMovements]([CompanyId],[Id]);');
        EXEC(N'DROP INDEX [IX_StockMovements_CompanyId_WarehouseId_ItemId_IdentityId] ON [StockMovements];');
        EXEC(N'CREATE UNIQUE INDEX [IX_StockMovements_CompanyId_WarehouseId_ItemId_IdentityId] ON [StockMovements] ([CompanyId], [WarehouseId], [ItemId], [IdentityId]) WHERE [ReversesMovementId] IS NULL;');
        EXEC(N'DROP INDEX [IX_StockMovements_CompanyId_IdentityId] ON [StockMovements];');
        EXEC(N'CREATE UNIQUE INDEX [IX_StockMovements_CompanyId_IdentityId] ON [StockMovements] ([CompanyId], [IdentityId]) WHERE [IsSerial] = 1 AND [ReversesMovementId] IS NULL;');
        EXEC(N'CREATE UNIQUE INDEX [IX_StockMovements_CompanyId_ReversesMovementId] ON [StockMovements] ([CompanyId], [ReversesMovementId]) WHERE [ReversesMovementId] IS NOT NULL;');
        EXEC(N'CREATE UNIQUE INDEX [IX_InventoryOpenings_CompanyId_ReversesOpeningId] ON [InventoryOpenings] ([CompanyId], [ReversesOpeningId]) WHERE [ReversesOpeningId] IS NOT NULL AND [State] <> 4;');
        INSERT INTO dbo.__AmeenSchemaVersions([Version],AppliedAt) VALUES(16,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
