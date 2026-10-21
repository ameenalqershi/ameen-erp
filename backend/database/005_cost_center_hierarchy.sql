-- COST01 hierarchy, responsibility and metadata. Apply after schema 004.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 4)
        THROW 51001, 'Apply schema 004 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 5)
    BEGIN
        EXEC(N'ALTER TABLE [CostCenters] ADD [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [ParentId] uniqueidentifier NULL, [ResponsibleMembershipId] uniqueidentifier NULL, [Revision] int NULL;');
        EXEC(N'UPDATE [CostCenters] SET [Revision]=1;');
        EXEC(N'ALTER TABLE [CostCenters] ALTER COLUMN [Revision] int NOT NULL;');
        EXEC(N'ALTER TABLE [CostCenters] ADD CONSTRAINT [CK_CostCenter_Revision] CHECK ([Revision] > 0);');
        EXEC(N'ALTER TABLE [CostCenters] ADD CONSTRAINT [CK_CostCenter_Parent] CHECK ([ParentId] IS NULL OR [ParentId] <> [Id]);');
        EXEC(N'ALTER TABLE [CostCenters] ADD CONSTRAINT [FK_CostCenters_CostCenters_CompanyId_ParentId] FOREIGN KEY ([CompanyId],[ParentId]) REFERENCES [CostCenters] ([CompanyId],[Id]);');
        EXEC(N'ALTER TABLE [CostCenters] ADD CONSTRAINT [FK_CostCenters_Memberships_CompanyId_ResponsibleMembershipId] FOREIGN KEY ([CompanyId],[ResponsibleMembershipId]) REFERENCES [Memberships] ([CompanyId],[Id]);');
        EXEC(N'CREATE INDEX [IX_CostCenters_CompanyId_ParentId] ON [CostCenters] ([CompanyId],[ParentId]);');
        EXEC(N'CREATE INDEX [IX_CostCenters_CompanyId_ResponsibleMembershipId] ON [CostCenters] ([CompanyId],[ResponsibleMembershipId]);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (5, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
