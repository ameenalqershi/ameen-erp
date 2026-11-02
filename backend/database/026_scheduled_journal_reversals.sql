-- GL03 scheduled reversal flags; existing rows remain manual, non-scheduled drafts/documents.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=25) THROW 51001,'Apply schema 025 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=26)
 BEGIN
  ALTER TABLE [Journals] ADD [AutomaticReversal] bit NOT NULL CONSTRAINT [DF_Journal_AutomaticReversal] DEFAULT(0),
   [AutomaticReversalDate] date NULL;
  -- Defer compilation until the columns exist; SQL Server otherwise compiles the whole batch first.
  EXEC sys.sp_executesql N'
   ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_AutomaticReversal] CHECK
    (([AutomaticReversal]=0 AND [AutomaticReversalDate] IS NULL) OR
     ([AutomaticReversal]=1 AND [AutomaticReversalDate] IS NOT NULL AND
      [AutomaticReversalDate]>=[AccountingDate] AND [Source]=0 AND [ReversesJournalId] IS NULL));
   CREATE INDEX [IX_Journals_CompanyId_AutomaticReversalDate_State_BranchId]
    ON [Journals]([CompanyId],[AutomaticReversalDate],[State],[BranchId]) WHERE [AutomaticReversal]=1;';
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(26,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
