-- SYS02 branch hierarchy and addresses. Unknown legacy addresses remain NULL.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 6)
        THROW 51001, 'Apply schema 006 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 7)
    BEGIN
        EXEC(N'ALTER TABLE [Branches] ADD [EnglishName] nvarchar(200) NULL, [Notes] nvarchar(2000) NULL,
            [City] nvarchar(200) NULL, [Address] nvarchar(500) NULL, [ParentId] uniqueidentifier NULL,
            [DefaultCostCenterId] uniqueidentifier NULL, [Revision] int NULL;');
        EXEC(N'UPDATE [Branches] SET [Revision]=1;');
        EXEC(N'ALTER TABLE [Branches] ALTER COLUMN [Revision] int NOT NULL;');
        EXEC(N'ALTER TABLE [Branches] ADD CONSTRAINT [CK_Branch_Revision] CHECK ([Revision] > 0);');
        EXEC(N'ALTER TABLE [Branches] ADD CONSTRAINT [FK_Branches_Branches_CompanyId_ParentId] FOREIGN KEY ([CompanyId],[ParentId]) REFERENCES [Branches] ([CompanyId],[Id]);');
        EXEC(N'ALTER TABLE [Branches] ADD CONSTRAINT [FK_Branches_CostCenters_CompanyId_DefaultCostCenterId] FOREIGN KEY ([CompanyId],[DefaultCostCenterId]) REFERENCES [CostCenters] ([CompanyId],[Id]);');
        EXEC(N'CREATE INDEX [IX_Branches_CompanyId_ParentId] ON [Branches] ([CompanyId],[ParentId]);');
        EXEC(N'CREATE INDEX [IX_Branches_CompanyId_DefaultCostCenterId] ON [Branches] ([CompanyId],[DefaultCostCenterId]);');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (7, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
