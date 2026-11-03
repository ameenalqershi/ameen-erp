-- CASH09 schema-only: cheque transition history row with exactly one source
-- cheque, never a clearing, rejection, bank mutation or financial posting.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lockResult int;
 EXEC @lockResult=sys.sp_getapplock @Resource=N'ameen:schema',
  @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lockResult<0 THROW 51000,'Schema lock unavailable',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=36)
  THROW 51001,'Apply schema 036 first',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.__AmeenSchemaVersions WHERE [Version]=37)
 BEGIN
  CREATE TABLE [ChequeMovements](
   [CompanyId] uniqueidentifier NOT NULL,
   [Id] uniqueidentifier NOT NULL,
   [Version] uniqueidentifier NOT NULL,
   [BranchId] uniqueidentifier NULL,
   [State] nvarchar(32) NOT NULL,
   [IncomingChequeId] uniqueidentifier NULL,
   [OutgoingChequeId] uniqueidentifier NULL,
   [Action] nvarchar(32) NOT NULL,
   [ActionDate] date NOT NULL,
   [ReceivedBankAccountId] uniqueidentifier NULL,
   [RejectionReason] nvarchar(1000) NULL,
   [Fee] decimal(20,6) NULL,
   [CreatedBy] nvarchar(200) NOT NULL,
   [CreatedAt] datetimeoffset(7) NOT NULL,
   [Revision] int NOT NULL,
   [LastModifiedAt] datetimeoffset(7) NOT NULL,
   CONSTRAINT [PK_ChequeMovements] PRIMARY KEY ([CompanyId],[Id]),
   CONSTRAINT [FK_ChequeMovement_Company] FOREIGN KEY ([CompanyId])
     REFERENCES [Companies]([Id]),
   CONSTRAINT [FK_ChequeMovement_Branch] FOREIGN KEY ([CompanyId],[BranchId])
     REFERENCES [Branches]([CompanyId],[Id]),
   CONSTRAINT [FK_ChequeMovement_Incoming] FOREIGN KEY ([CompanyId],[IncomingChequeId])
     REFERENCES [IncomingCheques]([CompanyId],[Id]),
   CONSTRAINT [FK_ChequeMovement_Outgoing] FOREIGN KEY ([CompanyId],[OutgoingChequeId])
     REFERENCES [OutgoingCheques]([CompanyId],[Id]),
   CONSTRAINT [FK_ChequeMovement_ReceivedBank] FOREIGN KEY ([CompanyId],[ReceivedBankAccountId])
     REFERENCES [BankAccounts]([CompanyId],[Id]),
   CONSTRAINT [CK_ChequeMovement_ExactlyOne] CHECK (
     ([IncomingChequeId] IS NOT NULL AND [OutgoingChequeId] IS NULL)
     OR ([IncomingChequeId] IS NULL AND [OutgoingChequeId] IS NOT NULL)),
   CONSTRAINT [CK_ChequeMovement_Revision] CHECK ([Revision]>0),
   CONSTRAINT [CK_ChequeMovement_AuditTime] CHECK ([CreatedAt]<=[LastModifiedAt]),
   CONSTRAINT [CK_ChequeMovement_Fee] CHECK ([Fee] IS NULL OR [Fee]>=0),
   CONSTRAINT [CK_ChequeMovement_Text] CHECK (
     LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([Action])))>0)
  );
  CREATE INDEX [IX_ChequeMovements_Company_Incoming]
    ON [ChequeMovements]([CompanyId],[IncomingChequeId]);
  CREATE INDEX [IX_ChequeMovements_Company_Outgoing]
    ON [ChequeMovements]([CompanyId],[OutgoingChequeId]);
  INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt])
    VALUES(37,SYSUTCDATETIME());
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
