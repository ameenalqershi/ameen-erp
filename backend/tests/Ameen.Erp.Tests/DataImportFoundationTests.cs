using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SYS13-01: no import is executed by constructing these schema-only objects.
public static class DataImportFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS13-01: import metadata, mapping lines and tenant audit preserve source fields", f =>
        {
            var now=new FixedClock().UtcNow;
            var beforeAudit=f.Store.Count<AuditEvent>();
            var beforeJournal=f.Store.Count<JournalEntry>();
            var b=new DataImportBatch(f.CompanyId,f.BranchId,"test-only",
                "unapproved-data-kind","file-external-id",new DateOnly(2026,10,31),
                "unapproved-duplicate-policy",f.Owner.Subject,now,
                new[] { ("external_id","target_id",(string?)null),
                        ("external_amount","target_amount","0") });
            Ensure(b.CompanyId==f.CompanyId && b.BranchId==f.BranchId &&
                b.StatusCode=="test-only" && b.DataKindCode=="unapproved-data-kind" &&
                b.FileIdentifier=="file-external-id" && b.CutoffDate==new DateOnly(2026,10,31) &&
                b.DuplicatePolicyCode=="unapproved-duplicate-policy" &&
                b.CreatedBy==f.Owner.Subject && b.CreatedAt==now &&
                b.LastModifiedAt==now && b.Revision==1 && b.Mappings.Count==2 &&
                b.Mappings.All(x=>x.CompanyId==f.CompanyId && x.BatchId==b.Id) &&
                b.Mappings[0].SourceColumn=="external_id" &&
                b.Mappings[0].TargetField=="target_id" &&
                b.Mappings[0].DefaultValue is null &&
                b.Mappings[1].DefaultValue=="0", "SYS13 source fields not preserved");
            Ensure(f.Store.Count<DataImportBatch>()==0 &&
                f.Store.Count<DataImportFieldMapping>()==0 &&
                f.Store.Count<AuditEvent>()==beforeAudit &&
                f.Store.Count<JournalEntry>()==beforeJournal,
                "SYS13 schema metadata performed an import or financial operation");
            return Task.CompletedTask;
        });
        test("SYS13-FG04: default import audit timestamp rejects before mapping materialization", f =>
        {
            var beforeAudit = f.Store.Count<AuditEvent>();
            Error("data_import_audit_time_invalid", () => new DataImportBatch(
                f.CompanyId, f.BranchId, "test-only", "unapproved-kind", "file-id",
                new DateOnly(2026, 10, 31), "unapproved-policy", f.Owner.Subject,
                default, new[] { ("source", "target", (string?)null) }));
            Ensure(f.Store.Count<DataImportBatch>() == 0 &&
                f.Store.Count<DataImportFieldMapping>() == 0 &&
                f.Store.Count<AuditEvent>() == beforeAudit &&
                f.Store.Count<JournalEntry>() == 0,
                "SYS13 rejected timestamp must not save import, mappings or ledger");
            return Task.CompletedTask;
        });
        test("SYS13-01: invalid required fields and source mapping rejected without writes", f =>
        {
            var now=new FixedClock().UtcNow;
            DataImportBatch Make(Guid companyId,Guid? branch,DateOnly cutoff,
                IReadOnlyList<(string,string,string?)> mappings) =>
                new(companyId,branch,"test-only","kind","file",cutoff,
                    "duplicate-policy",f.Owner.Subject,now,mappings);
            var valid=new[] { ("source","target",(string?)null) };
            Error("company_required",()=>Make(Guid.Empty,null,new DateOnly(2026,10,31),valid));
            Error("data_import_branch_invalid",()=>Make(f.CompanyId,Guid.Empty,new DateOnly(2026,10,31),valid));
            Error("data_import_cutoff_required",()=>Make(f.CompanyId,null,DateOnly.MinValue,valid));
            Error("data_import_mappings_required",()=>Make(f.CompanyId,null,new DateOnly(2026,10,31),
                Array.Empty<(string,string,string?)>()));
            Error("data_import_batch_required",()=>new DataImportFieldMapping(
                f.CompanyId,Guid.Empty,"source","target",null));
            Error("invalid_text",()=>new DataImportFieldMapping(
                f.CompanyId,Guid.NewGuid(),"","target",null));
            Error("invalid_text",()=>new DataImportFieldMapping(
                f.CompanyId,Guid.NewGuid(),"source"," ",null));
            Ensure(f.Store.Count<DataImportBatch>()==0,"SYS13 invalid metadata stored");
            return Task.CompletedTask;
        });
    }

    private static void Error(string expected,Action operation)
    {
        try { operation(); }
        catch(BusinessException ex) when(ex.Code==expected) { return; }
        throw new Exception("Expected SYS13 "+expected);
    }
    private static void Ensure(bool valid,string message)
    {
        if(!valid) throw new Exception(message);
    }
}
