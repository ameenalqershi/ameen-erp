SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=14) THROW 51001, 'Apply schema 014 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=15)
    BEGIN
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD [JournalId] uniqueidentifier NULL, [InventoryAccountId] uniqueidentifier NULL, [ClearingAccountId] uniqueidentifier NULL, [PostingSettingsVersion] uniqueidentifier NULL, [PostedBy] nvarchar(200) NULL, [PostedAt] datetimeoffset NULL;');
        EXEC(N'ALTER TABLE [Journals] ADD [InventoryOpeningId] uniqueidentifier NULL;');
        EXEC(N'ALTER TABLE [InventoryOpenings] DROP CONSTRAINT [CK_InventoryOpening_State];');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [CK_InventoryOpening_State] CHECK ([State] BETWEEN 0 AND 5);');
        EXEC(N'ALTER TABLE [InventoryOpenings] DROP CONSTRAINT [CK_InventoryOpening_Approval];');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [CK_InventoryOpening_Approval] CHECK (([State] IN (3,5) AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> [CreatedBy]) OR ([State] NOT IN (3,5) AND [ApprovedBy] IS NULL));');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [CK_InventoryOpening_Posted] CHECK (([State] = 5 AND [JournalId] IS NOT NULL AND [InventoryAccountId] IS NOT NULL AND [ClearingAccountId] IS NOT NULL AND [PostingSettingsVersion] IS NOT NULL AND [PostedBy] IS NOT NULL AND [PostedAt] IS NOT NULL) OR ([State] <> 5 AND [JournalId] IS NULL AND [InventoryAccountId] IS NULL AND [ClearingAccountId] IS NULL AND [PostingSettingsVersion] IS NULL AND [PostedBy] IS NULL AND [PostedAt] IS NULL));');
        EXEC(N'ALTER TABLE [Journals] DROP CONSTRAINT [CK_Journal_Source];');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_Source] CHECK (([Source] = 0 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 1 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NOT NULL AND [SourceTemplateVersion] IS NOT NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 2 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NOT NULL) OR ([Source] = 3 AND [InventoryOpeningId] IS NOT NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL));');
        EXEC(N'CREATE TABLE [InventoryOpeningPostingSettings] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [ClearingAccountId] uniqueidentifier NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_InventoryOpeningPostingSettings] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_InventoryOpeningPostingSettings_Accounts_CompanyId_ClearingAccountId] FOREIGN KEY ([CompanyId], [ClearingAccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
    CONSTRAINT [FK_InventoryOpeningPostingSettings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);');
        EXEC(N'CREATE TABLE [StockMovements] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [OpeningId] uniqueidentifier NOT NULL,
    [JournalId] uniqueidentifier NOT NULL,
    [BranchId] uniqueidentifier NOT NULL,
    [WarehouseId] uniqueidentifier NOT NULL,
    [ItemId] uniqueidentifier NOT NULL,
    [IdentityId] uniqueidentifier NULL,
    [IsSerial] bit NOT NULL,
    [InventoryAccountId] uniqueidentifier NOT NULL,
    [BaseQuantity] decimal(20,6) NOT NULL,
    [Value] decimal(20,6) NOT NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [AccountingDate] date NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_StockMovements] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_StockMovement_Amounts] CHECK ([BaseQuantity] > 0 AND [Value] >= 0),
    CONSTRAINT [CK_StockMovement_Serial] CHECK ([IsSerial] = 0 OR ([IdentityId] IS NOT NULL AND [BaseQuantity] = 1)),
    CONSTRAINT [FK_StockMovements_Accounts_CompanyId_InventoryAccountId] FOREIGN KEY ([CompanyId], [InventoryAccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
    CONSTRAINT [FK_StockMovements_Branches_CompanyId_BranchId] FOREIGN KEY ([CompanyId], [BranchId]) REFERENCES [Branches] ([CompanyId], [Id]),
    CONSTRAINT [FK_StockMovements_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_StockMovements_InventoryIdentities_CompanyId_IdentityId] FOREIGN KEY ([CompanyId], [IdentityId]) REFERENCES [InventoryIdentities] ([CompanyId], [Id]),
    CONSTRAINT [FK_StockMovements_InventoryOpenings_CompanyId_OpeningId] FOREIGN KEY ([CompanyId], [OpeningId]) REFERENCES [InventoryOpenings] ([CompanyId], [Id]),
    CONSTRAINT [FK_StockMovements_Items_CompanyId_ItemId] FOREIGN KEY ([CompanyId], [ItemId]) REFERENCES [Items] ([CompanyId], [Id]),
    CONSTRAINT [FK_StockMovements_Journals_CompanyId_JournalId] FOREIGN KEY ([CompanyId], [JournalId]) REFERENCES [Journals] ([CompanyId], [Id]),
    CONSTRAINT [FK_StockMovements_Warehouses_CompanyId_WarehouseId] FOREIGN KEY ([CompanyId], [WarehouseId]) REFERENCES [Warehouses] ([CompanyId], [Id])
);');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [FK_Journals_InventoryOpenings_CompanyId_InventoryOpeningId] FOREIGN KEY ([CompanyId],[InventoryOpeningId]) REFERENCES [InventoryOpenings]([CompanyId],[Id]);');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [FK_InventoryOpenings_Accounts_CompanyId_InventoryAccountId] FOREIGN KEY ([CompanyId],[InventoryAccountId]) REFERENCES [Accounts]([CompanyId],[Id]);');
        EXEC(N'ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [FK_InventoryOpenings_Accounts_CompanyId_ClearingAccountId] FOREIGN KEY ([CompanyId],[ClearingAccountId]) REFERENCES [Accounts]([CompanyId],[Id]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_InventoryOpeningPostingSettings_CompanyId] ON [InventoryOpeningPostingSettings] ([CompanyId]);');
        EXEC(N'CREATE INDEX [IX_InventoryOpeningPostingSettings_CompanyId_ClearingAccountId] ON [InventoryOpeningPostingSettings] ([CompanyId], [ClearingAccountId]);');
        EXEC(N'CREATE INDEX [IX_InventoryOpenings_CompanyId_ClearingAccountId] ON [InventoryOpenings] ([CompanyId], [ClearingAccountId]);');
        EXEC(N'CREATE INDEX [IX_InventoryOpenings_CompanyId_InventoryAccountId] ON [InventoryOpenings] ([CompanyId], [InventoryAccountId]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_InventoryOpenings_CompanyId_JournalId] ON [InventoryOpenings] ([CompanyId], [JournalId]) WHERE [JournalId] IS NOT NULL;');
        EXEC(N'CREATE UNIQUE INDEX [IX_Journals_CompanyId_InventoryOpeningId] ON [Journals] ([CompanyId], [InventoryOpeningId]) WHERE [InventoryOpeningId] IS NOT NULL;');
        EXEC(N'CREATE INDEX [IX_StockMovements_CompanyId_BranchId_AccountingDate] ON [StockMovements] ([CompanyId], [BranchId], [AccountingDate]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_StockMovements_CompanyId_IdentityId] ON [StockMovements] ([CompanyId], [IdentityId]) WHERE [IsSerial] = 1;');
        EXEC(N'CREATE INDEX [IX_StockMovements_CompanyId_InventoryAccountId] ON [StockMovements] ([CompanyId], [InventoryAccountId]);');
        EXEC(N'CREATE INDEX [IX_StockMovements_CompanyId_ItemId] ON [StockMovements] ([CompanyId], [ItemId]);');
        EXEC(N'CREATE INDEX [IX_StockMovements_CompanyId_JournalId] ON [StockMovements] ([CompanyId], [JournalId]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_StockMovements_CompanyId_OpeningId_ItemId_IdentityId] ON [StockMovements] ([CompanyId], [OpeningId], [ItemId], [IdentityId]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_StockMovements_CompanyId_WarehouseId_ItemId_IdentityId] ON [StockMovements] ([CompanyId], [WarehouseId], [ItemId], [IdentityId]);');
        INSERT INTO dbo.__AmeenSchemaVersions([Version],AppliedAt) VALUES(15,SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
