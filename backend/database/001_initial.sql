-- Ameen ERP schema 001: general-ledger foundation. SQL Server 2022+.
-- Generated from the reviewed .NET 10 / EF Core 10 mapping.
-- Apply to a dedicated empty database with a migration identity, not the API identity.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF OBJECT_ID(N'dbo.__AmeenSchemaVersions', N'U') IS NULL
        CREATE TABLE dbo.__AmeenSchemaVersions ([Version] int NOT NULL PRIMARY KEY, AppliedAt datetimeoffset NOT NULL);
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 1)
    BEGIN
CREATE TABLE [Companies] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [CountryCode] nvarchar(2) NOT NULL,
    [FunctionalCurrency] nvarchar(3) NOT NULL,
    [CurrencyScale] int NOT NULL,
    [RequireSeparateApprover] bit NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Company_Scale] CHECK ([CurrencyScale] BETWEEN 0 AND 6)
);


CREATE TABLE [AuditEvents] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [ActorId] nvarchar(200) NOT NULL,
    [Action] nvarchar(100) NOT NULL,
    [ResourceId] uniqueidentifier NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Details] nvarchar(max) NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_AuditEvents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [Branches] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Branches] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_Branches_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [Currencies] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Code] nvarchar(3) NOT NULL,
    [Scale] int NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Currencies] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [AK_Currencies_CompanyId_Code] UNIQUE ([CompanyId], [Code]),
    CONSTRAINT [CK_Currency_Scale] CHECK ([Scale] BETWEEN 0 AND 6),
    CONSTRAINT [FK_Currencies_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [FiscalYears] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Code] nvarchar(16) NOT NULL,
    [StartsOn] date NOT NULL,
    [EndsOn] date NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_FiscalYears] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_FiscalYear_Dates] CHECK ([EndsOn] >= [StartsOn]),
    CONSTRAINT [FK_FiscalYears_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [IdempotencyRecords] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Key] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Operation] nvarchar(120) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ActorId] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Fingerprint] nvarchar(64) NOT NULL,
    [ReceiptJson] nvarchar(max) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_IdempotencyRecords_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [Memberships] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Subject] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [GrantedPermissions] nvarchar(max) NOT NULL,
    [BranchIds] nvarchar(max) NOT NULL,
    [AllBranches] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Memberships] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_Memberships_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [NumberSequences] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Scope] nvarchar(80) NOT NULL,
    [LastNumber] bigint NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_NumberSequences] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_Sequence_Positive] CHECK ([LastNumber] > 0),
    CONSTRAINT [FK_NumberSequences_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [OutboxEvents] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Type] nvarchar(100) NOT NULL,
    [ResourceId] uniqueidentifier NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [DeliveredAt] datetimeoffset NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_OutboxEvents] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_OutboxEvents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [CostCenters] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [BranchId] uniqueidentifier NULL,
    [IsActive] bit NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_CostCenters] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [FK_CostCenters_Branches_CompanyId_BranchId] FOREIGN KEY ([CompanyId], [BranchId]) REFERENCES [Branches] ([CompanyId], [Id]),
    CONSTRAINT [FK_CostCenters_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


CREATE TABLE [Accounts] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Type] int NOT NULL,
    [ParentId] uniqueidentifier NULL,
    [IsPosting] bit NOT NULL,
    [IsControl] bit NOT NULL,
    [RequiresCostCenter] bit NOT NULL,
    [CurrencyCode] nvarchar(3) NULL,
    [IsActive] bit NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Accounts] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_Account_Parent] CHECK ([ParentId] IS NULL OR [ParentId] <> [Id]),
    CONSTRAINT [FK_Accounts_Accounts_CompanyId_ParentId] FOREIGN KEY ([CompanyId], [ParentId]) REFERENCES [Accounts] ([CompanyId], [Id]),
    CONSTRAINT [FK_Accounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_Accounts_Currencies_CompanyId_CurrencyCode] FOREIGN KEY ([CompanyId], [CurrencyCode]) REFERENCES [Currencies] ([CompanyId], [Code])
);


CREATE TABLE [ExchangeRates] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [EffectiveDate] date NOT NULL,
    [RateToFunctional] decimal(20,10) NOT NULL,
    [Source] nvarchar(200) NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ExchangeRates] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_ExchangeRate_Positive] CHECK ([RateToFunctional] > 0),
    CONSTRAINT [FK_ExchangeRates_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_ExchangeRates_Currencies_CompanyId_CurrencyCode] FOREIGN KEY ([CompanyId], [CurrencyCode]) REFERENCES [Currencies] ([CompanyId], [Code])
);


CREATE TABLE [FiscalPeriods] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [FiscalYearId] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [StartsOn] date NOT NULL,
    [EndsOn] date NOT NULL,
    [IsClosed] bit NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_FiscalPeriods] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_Period_Dates] CHECK ([EndsOn] >= [StartsOn]),
    CONSTRAINT [FK_FiscalPeriods_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_FiscalPeriods_FiscalYears_CompanyId_FiscalYearId] FOREIGN KEY ([CompanyId], [FiscalYearId]) REFERENCES [FiscalYears] ([CompanyId], [Id])
);


CREATE TABLE [Journals] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [BranchId] uniqueidentifier NOT NULL,
    [FiscalPeriodId] uniqueidentifier NOT NULL,
    [Number] nvarchar(64) NOT NULL,
    [Sequence] bigint NOT NULL,
    [DocumentDate] date NOT NULL,
    [AccountingDate] date NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [ExternalReference] nvarchar(100) COLLATE Latin1_General_100_BIN2 NULL,
    [State] int NOT NULL,
    [CreatedBy] nvarchar(200) NOT NULL,
    [ApprovedBy] nvarchar(200) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [PostedAt] datetimeoffset NULL,
    [ReversesJournalId] uniqueidentifier NULL,
    [ReversalJournalId] uniqueidentifier NULL,
    [PendingReversalJournalId] uniqueidentifier NULL,
    [ReversalReason] nvarchar(500) NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Journals] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_Journal_State] CHECK ([State] BETWEEN 0 AND 5),
    CONSTRAINT [FK_Journals_Branches_CompanyId_BranchId] FOREIGN KEY ([CompanyId], [BranchId]) REFERENCES [Branches] ([CompanyId], [Id]),
    CONSTRAINT [FK_Journals_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_Journals_FiscalPeriods_CompanyId_FiscalPeriodId] FOREIGN KEY ([CompanyId], [FiscalPeriodId]) REFERENCES [FiscalPeriods] ([CompanyId], [Id]),
    CONSTRAINT [FK_Journals_Journals_CompanyId_ReversesJournalId] FOREIGN KEY ([CompanyId], [ReversesJournalId]) REFERENCES [Journals] ([CompanyId], [Id])
);


CREATE TABLE [JournalLines] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [JournalId] uniqueidentifier NOT NULL,
    [Position] int NOT NULL,
    [AccountId] uniqueidentifier NOT NULL,
    [CostCenterId] uniqueidentifier NULL,
    [Description] nvarchar(500) NOT NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [ExchangeRateId] uniqueidentifier NULL,
    [ExchangeRate] decimal(20,10) NOT NULL,
    [Debit] decimal(20,6) NOT NULL,
    [Credit] decimal(20,6) NOT NULL,
    [FunctionalDebit] decimal(20,6) NOT NULL,
    [FunctionalCredit] decimal(20,6) NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_JournalLines] PRIMARY KEY ([CompanyId], [Id]),
    CONSTRAINT [CK_Line_Amounts] CHECK (([Debit] > 0 AND [Credit] = 0 AND [FunctionalDebit] > 0 AND [FunctionalCredit] = 0) OR ([Credit] > 0 AND [Debit] = 0 AND [FunctionalCredit] > 0 AND [FunctionalDebit] = 0)),
    CONSTRAINT [CK_Line_Rate] CHECK ([ExchangeRate] > 0),
    CONSTRAINT [FK_JournalLines_Accounts_CompanyId_AccountId] FOREIGN KEY ([CompanyId], [AccountId]) REFERENCES [Accounts] ([CompanyId], [Id]),
    CONSTRAINT [FK_JournalLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_JournalLines_CostCenters_CompanyId_CostCenterId] FOREIGN KEY ([CompanyId], [CostCenterId]) REFERENCES [CostCenters] ([CompanyId], [Id]),
    CONSTRAINT [FK_JournalLines_Currencies_CompanyId_CurrencyCode] FOREIGN KEY ([CompanyId], [CurrencyCode]) REFERENCES [Currencies] ([CompanyId], [Code]),
    CONSTRAINT [FK_JournalLines_ExchangeRates_CompanyId_ExchangeRateId] FOREIGN KEY ([CompanyId], [ExchangeRateId]) REFERENCES [ExchangeRates] ([CompanyId], [Id]),
    CONSTRAINT [FK_JournalLines_Journals_CompanyId_JournalId] FOREIGN KEY ([CompanyId], [JournalId]) REFERENCES [Journals] ([CompanyId], [Id]) ON DELETE CASCADE
);


CREATE UNIQUE INDEX [IX_Accounts_CompanyId_Code] ON [Accounts] ([CompanyId], [Code]);


CREATE INDEX [IX_Accounts_CompanyId_CurrencyCode] ON [Accounts] ([CompanyId], [CurrencyCode]);


CREATE INDEX [IX_Accounts_CompanyId_ParentId] ON [Accounts] ([CompanyId], [ParentId]);


CREATE INDEX [IX_AuditEvents_CompanyId_OccurredAt_ResourceId] ON [AuditEvents] ([CompanyId], [OccurredAt], [ResourceId]);


CREATE UNIQUE INDEX [IX_Branches_CompanyId_Code] ON [Branches] ([CompanyId], [Code]);


CREATE UNIQUE INDEX [IX_Companies_Code] ON [Companies] ([Code]);


CREATE INDEX [IX_CostCenters_CompanyId_BranchId] ON [CostCenters] ([CompanyId], [BranchId]);


CREATE UNIQUE INDEX [IX_CostCenters_CompanyId_Code] ON [CostCenters] ([CompanyId], [Code]);


CREATE UNIQUE INDEX [IX_ExchangeRates_CompanyId_CurrencyCode_EffectiveDate] ON [ExchangeRates] ([CompanyId], [CurrencyCode], [EffectiveDate]);


CREATE UNIQUE INDEX [IX_FiscalPeriods_CompanyId_Code] ON [FiscalPeriods] ([CompanyId], [Code]);


CREATE INDEX [IX_FiscalPeriods_CompanyId_FiscalYearId] ON [FiscalPeriods] ([CompanyId], [FiscalYearId]);


CREATE INDEX [IX_FiscalPeriods_CompanyId_StartsOn_EndsOn] ON [FiscalPeriods] ([CompanyId], [StartsOn], [EndsOn]);


CREATE UNIQUE INDEX [IX_FiscalYears_CompanyId_Code] ON [FiscalYears] ([CompanyId], [Code]);


CREATE UNIQUE INDEX [IX_IdempotencyRecords_CompanyId_Key] ON [IdempotencyRecords] ([CompanyId], [Key]);


CREATE INDEX [IX_JournalLines_CompanyId_AccountId_JournalId] ON [JournalLines] ([CompanyId], [AccountId], [JournalId]);


CREATE INDEX [IX_JournalLines_CompanyId_CostCenterId] ON [JournalLines] ([CompanyId], [CostCenterId]);


CREATE INDEX [IX_JournalLines_CompanyId_CurrencyCode] ON [JournalLines] ([CompanyId], [CurrencyCode]);


CREATE INDEX [IX_JournalLines_CompanyId_ExchangeRateId] ON [JournalLines] ([CompanyId], [ExchangeRateId]);


CREATE UNIQUE INDEX [IX_JournalLines_CompanyId_JournalId_Position] ON [JournalLines] ([CompanyId], [JournalId], [Position]);


CREATE INDEX [IX_Journals_CompanyId_BranchId_AccountingDate_State] ON [Journals] ([CompanyId], [BranchId], [AccountingDate], [State]);


CREATE INDEX [IX_Journals_CompanyId_FiscalPeriodId_State] ON [Journals] ([CompanyId], [FiscalPeriodId], [State]);


CREATE UNIQUE INDEX [IX_Journals_CompanyId_Number] ON [Journals] ([CompanyId], [Number]);


CREATE INDEX [IX_Journals_CompanyId_ReversesJournalId] ON [Journals] ([CompanyId], [ReversesJournalId]);


CREATE UNIQUE INDEX [IX_Memberships_CompanyId_Subject] ON [Memberships] ([CompanyId], [Subject]);


CREATE UNIQUE INDEX [IX_NumberSequences_CompanyId_Scope] ON [NumberSequences] ([CompanyId], [Scope]);


CREATE UNIQUE INDEX [IX_OutboxEvents_CompanyId_ResourceId_Type] ON [OutboxEvents] ([CompanyId], [ResourceId], [Type]);


CREATE INDEX [IX_OutboxEvents_DeliveredAt_OccurredAt] ON [OutboxEvents] ([DeliveredAt], [OccurredAt]);


        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (1, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
