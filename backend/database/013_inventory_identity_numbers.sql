SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'ameen:schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
    IF @lockResult < 0 THROW 51000, 'Schema lock unavailable', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 12) THROW 51001, 'Apply schema 012 first', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version] = 13)
    BEGIN
        ALTER TABLE [InventoryIdentities] ADD [LotNumber] nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL, [SerialNumber] nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL;
        EXEC(N'UPDATE [InventoryIdentities] SET [LotNumber] = CASE WHEN [Tracking]=1 THEN [Code] END, [SerialNumber] = CASE WHEN [Tracking]=2 THEN [Code] END;');
        EXEC(N'ALTER TABLE [InventoryIdentities] ADD CONSTRAINT [CK_InventoryIdentity_Number] CHECK (([Tracking] = 1 AND [LotNumber] IS NOT NULL AND LEN([LotNumber]) > 0 AND [SerialNumber] IS NULL) OR ([Tracking] = 2 AND [SerialNumber] IS NOT NULL AND LEN([SerialNumber]) > 0 AND [LotNumber] IS NULL));');
        EXEC(N'CREATE UNIQUE INDEX [IX_InventoryIdentities_CompanyId_ItemId_LotNumber] ON [InventoryIdentities] ([CompanyId], [ItemId], [LotNumber]) WHERE [LotNumber] IS NOT NULL;');
        EXEC(N'CREATE UNIQUE INDEX [IX_InventoryIdentities_CompanyId_ItemId_SerialNumber] ON [InventoryIdentities] ([CompanyId], [ItemId], [SerialNumber]) WHERE [SerialNumber] IS NOT NULL;');
        INSERT INTO dbo.__AmeenSchemaVersions ([Version], AppliedAt) VALUES (13, SYSUTCDATETIME());
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
