using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SYS15-01 metadata only; no production backup or restore operation.
public static class BackupConfigurationFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS15-01: backup configuration retains tenant, encryption flag and audit only", f =>
        {
            var now=new FixedClock().UtcNow;
            var audit=f.Store.Count<AuditEvent>();
            var item=new BackupJobConfiguration(f.CompanyId,f.BranchId,"test-only",
                "نسخة اختبارية","no-execution-schedule","no-execution-destination",
                7,true,"test-run-ref",f.Owner.Subject,now);
            Ensure(item.CompanyId==f.CompanyId && item.BranchId==f.BranchId &&
                item.StatusCode=="test-only" && item.Name=="نسخة اختبارية" &&
                item.ScheduleText=="no-execution-schedule" &&
                item.Destination=="no-execution-destination" &&
                item.RetentionPolicy==7 && item.IsEncrypted &&
                item.LastTestReference=="test-run-ref" &&
                item.CreatedBy==f.Owner.Subject && item.CreatedAt==now &&
                item.LastModifiedAt==now && item.Revision==1,
                "SYS15 source field mismatch");
            Ensure(f.Store.Count<BackupJobConfiguration>()==0 &&
                f.Store.Count<AuditEvent>()==audit,
                "SYS15 schema-only card scheduled or executed a real backup");
            return Task.CompletedTask;
        });
        test("SYS15-01: blank backup scope, retention, name and references reject", f =>
        {
            var now=new FixedClock().UtcNow;
            BackupJobConfiguration Make(Guid cid,Guid? branch,string name,int retention) =>
                new(cid,branch,"test-only",name,"no-schedule","no-destination",
                    retention,false,null,f.Owner.Subject,now);
            Error("company_required",()=>Make(Guid.Empty,null,"job",0));
            Error("backup_branch_invalid",()=>Make(f.CompanyId,Guid.Empty,"job",0));
            Error("backup_retention_invalid",()=>Make(f.CompanyId,null,"job",-1));
            Error("invalid_text",()=>Make(f.CompanyId,null,"",0));
            Error("invalid_text",()=>new BackupJobConfiguration(f.CompanyId,null,
                "test-only","job"," ", "destination", 3, true,null,f.Owner.Subject,now));
            Error("invalid_text",()=>new BackupJobConfiguration(f.CompanyId,null,
                "test-only","job","schedule", " ", 3, true,null,f.Owner.Subject,now));
            var valid=Make(f.CompanyId,null,"job",0);
            Ensure(valid.LastTestReference is null && !valid.IsEncrypted &&
                valid.RetentionPolicy==0 && f.Store.Count<BackupJobConfiguration>()==0,
                "SYS15 optional reference and form fields altered");
            return Task.CompletedTask;
        });
    }

    private static void Error(string code,Action work)
    {
        try { work(); }
        catch(BusinessException ex) when(ex.Code==code) { return; }
        throw new Exception("Expected SYS15 "+code);
    }
    private static void Ensure(bool ok,string error)
    {
        if(!ok) throw new Exception(error);
    }
}
