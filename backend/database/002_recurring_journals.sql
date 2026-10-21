-- Ameen ERP schema 002: recurring journal templates and immutable journal provenance.
-- Upgrade an existing schema 001 database; retain 001_initial.sql unchanged.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 1)
        THROW 51001, 'Apply schema 001 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 2)
    BEGIN
CREATE TABLE [RecurringJournalTemplates] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [BranchId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Frequency] int NOT NULL,
    [GenerationMode] int NOT NULL,
    [StartsOn] date NOT NULL,
    [EndsOn] date NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] nvarchar(200) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_RecurringJournalTemplates] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_Recurring_Dates] CHECK ([EndsOn] >= [StartsOn]),
    CONSTRAINT [CK_Recurring_Frequency] CHECK ([Frequency] BETWEEN 0 AND 2),
    CONSTRAINT [CK_Recurring_Mode] CHECK ([GenerationMode] = 0),
    CONSTRAINT [FK_RecurringJournalTemplates_Branches_CompanyId_BranchId] FOREIGN KEY ([CompanyId], [BranchId]) REFERENCES [Branches] ([CompanyId], [Id]),
    CONSTRAINT [FK_RecurringJournalTemplates_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);

CREATE TABLE [RecurringJournalLines] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [TemplateId] uniqueidentifier NOT NULL,
    [Position] int NOT NULL,
    [AccountId] uniqueidentifier NOT NULL,
    [CostCenterId] uniqueidentifier NULL,
    [Description] nvarchar(500) NOT NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [Debit] decimal(20,6) NOT NULL,
    [Credit] decimal(20,6) NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_RecurringJournalLines] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_RecurringLine_Amounts] CHECK (([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)),
    CONSTRAINT [CK_RecurringLine_Position] CHECK ([Position] BETWEEN 1 AND 200),
    CONSTRAINT [FK_RecurringJournalLines_Accounts_CompanyId_AccountId] FOREIGN KEY ([CompanyId], [AccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
    CONSTRAINT [FK_RecurringJournalLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_RecurringJournalLines_CostCenters_CompanyId_CostCenterId] FOREIGN KEY ([CompanyId], [CostCenterId]) REFERENCES [CostCenters] ([CompanyId], [Id]),
    CONSTRAINT [FK_RecurringJournalLines_Currencies_CompanyId_CurrencyCode] FOREIGN KEY ([CompanyId], [CurrencyCode]) REFERENCES [Currencies] ([CompanyId], [Code]),
    CONSTRAINT [FK_RecurringJournalLines_RecurringJournalTemplates_CompanyId_TemplateId] FOREIGN KEY ([CompanyId], [TemplateId]) REFERENCES [RecurringJournalTemplates] ([CompanyId], [Id]) ON DELETE CASCADE
);

CREATE TABLE [RecurringJournalGenerations] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [TemplateId] uniqueidentifier NOT NULL,
    [TemplateVersion] uniqueidentifier NOT NULL,
    [FiscalPeriodId] uniqueidentifier NOT NULL,
    [JournalId] uniqueidentifier NOT NULL,
    [DueDate] date NOT NULL,
    [GeneratedBy] nvarchar(200) NOT NULL,
    [GeneratedAt] datetimeoffset NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_RecurringJournalGenerations] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_RecurringJournalGenerations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_RecurringJournalGenerations_FiscalPeriods_CompanyId_FiscalPeriodId] FOREIGN KEY ([CompanyId], [FiscalPeriodId]) REFERENCES [FiscalPeriods] ([CompanyId], [Id]),
    CONSTRAINT [FK_RecurringJournalGenerations_Journals_CompanyId_JournalId] FOREIGN KEY ([CompanyId], [JournalId]) REFERENCES [Journals] ([CompanyId], [Id]),
    CONSTRAINT [FK_RecurringJournalGenerations_RecurringJournalTemplates_CompanyId_TemplateId] FOREIGN KEY ([CompanyId], [TemplateId]) REFERENCES [RecurringJournalTemplates] ([CompanyId], [Id])
);
        EXEC(N'ALTER TABLE [Journals] ADD [Source] int NOT NULL CONSTRAINT [DF_Journals_Source] DEFAULT 0, [RecurringTemplateId] uniqueidentifier NULL, [SourceTemplateVersion] uniqueidentifier NULL;');
        -- Backfill historical reversals without changing amounts, dates or versions.
        EXEC(N'UPDATE [Journals] SET [Source] = 2 WHERE [ReversesJournalId] IS NOT NULL;');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [FK_Journals_RecurringJournalTemplates_CompanyId_RecurringTemplateId] FOREIGN KEY ([CompanyId], [RecurringTemplateId]) REFERENCES [RecurringJournalTemplates] ([CompanyId], [Id]);');
        EXEC(N'ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_Source] CHECK (([Source] = 0 AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 1 AND [RecurringTemplateId] IS NOT NULL AND [SourceTemplateVersion] IS NOT NULL AND [ReversesJournalId] IS NULL) OR ([Source] = 2 AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NOT NULL));');
        EXEC(N'CREATE INDEX [IX_Journals_CompanyId_RecurringTemplateId] ON [Journals] ([CompanyId], [RecurringTemplateId]);');
        EXEC(N'CREATE INDEX [IX_RecurringJournalGenerations_CompanyId_FiscalPeriodId] ON [RecurringJournalGenerations] ([CompanyId], [FiscalPeriodId]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_RecurringJournalGenerations_CompanyId_JournalId] ON [RecurringJournalGenerations] ([CompanyId], [JournalId]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_RecurringJournalGenerations_CompanyId_TemplateId_FiscalPeriodId] ON [RecurringJournalGenerations] ([CompanyId], [TemplateId], [FiscalPeriodId]);');
        EXEC(N'CREATE INDEX [IX_RecurringJournalLines_CompanyId_AccountId] ON [RecurringJournalLines] ([CompanyId], [AccountId]);');
        EXEC(N'CREATE INDEX [IX_RecurringJournalLines_CompanyId_CostCenterId] ON [RecurringJournalLines] ([CompanyId], [CostCenterId]);');
        EXEC(N'CREATE INDEX [IX_RecurringJournalLines_CompanyId_CurrencyCode] ON [RecurringJournalLines] ([CompanyId], [CurrencyCode]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_RecurringJournalLines_CompanyId_TemplateId_Position] ON [RecurringJournalLines] ([CompanyId], [TemplateId], [Position]);');
        EXEC(N'CREATE UNIQUE INDEX [IX_RecurringJournalTemplates_CompanyId_BranchId_Name] ON [RecurringJournalTemplates] ([CompanyId], [BranchId], [Name]);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (2, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
