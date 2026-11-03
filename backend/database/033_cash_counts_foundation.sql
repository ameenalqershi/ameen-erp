-- CASH11 structural-only foundation: no drawer balance, variance adjustment,
-- approval, ledger posting or CashBox mutation. Keep application writes blocked.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult < 0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=32)
  THROW 51001,'Apply schema 032 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=33)
 BEGIN
  CREATE TABLE [CashCounts](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,
   [State] nvarchar(32) NOT NULL,
   [CashBoxId] uniqueidentifier NOT NULL,
   [CutoffAt] datetimeoffset(7) NOT NULL,
   [CountedByMembershipId] uniqueidentifier NOT NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_CashCounts] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashCount_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashCount_Branch] FOREIGN KEY ([CompanyId],[BranchId]) REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_CashCount_CashBox] FOREIGN KEY ([CompanyId],[CashBoxId]) REFERENCES [CashBoxes]([CompanyId],[Id]),
   CONSTRAINT [FK_CashCount_Counter] FOREIGN KEY ([CompanyId],[CountedByMembershipId]) REFERENCES [Memberships]([CompanyId],[Id]),
   CONSTRAINT [CK_CashCount_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_CashCount_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_CashCount_StateNonempty] CHECK (LEN(LTRIM(RTRIM([State])))>0)
  );
  CREATE INDEX [IX_CashCounts_CompanyId_CashBoxId_CutoffAt]
    ON [CashCounts]([CompanyId],[CashBoxId],[CutoffAt]);

  CREATE TABLE [CashCountLines](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [CashCountId] uniqueidentifier NOT NULL,
   [Denomination] decimal(20,6) NOT NULL,
   [PieceCount] int NOT NULL,
   -- The system owns counted value, not the caller.
   [CountedValue] AS CONVERT(decimal(20,6),[Denomination]*[PieceCount]) PERSISTED,
   CONSTRAINT [PK_CashCountLines] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_CashCountLine_Company] FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_CashCountLine_Count] FOREIGN KEY ([CompanyId],[CashCountId]) REFERENCES [CashCounts]([CompanyId],[Id]),
   CONSTRAINT [CK_CashCountLine_Values] CHECK ([Denomination]>0 AND [PieceCount]>=0)
  );
  CREATE INDEX [IX_CashCountLines_CompanyId_CashCountId]
    ON [CashCountLines]([CompanyId],[CashCountId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(33,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
