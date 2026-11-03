using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SYS12-01 tests only schema/domain invariants, not print rendering.
public static class PrintTemplateFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS12-01: draft template fields, tenant scope and audit stay exact", f =>
        {
            var now=new FixedClock().UtcNow;
            var beforeJournals=f.Store.Count<JournalEntry>();
            var beforeAudits=f.Store.Count<AuditEvent>();
            var item=new PrintTemplate(f.CompanyId, f.BranchId,
                "test-draft", "unapproved-invoice-kind", "unapproved-language",
                "unapproved-paper-size", "قالب تجريبي", "V1", true, f.Owner.Subject, now);
            Ensure(item.CompanyId==f.CompanyId && item.Id!=Guid.Empty &&
                item.BranchId==f.BranchId && item.StatusCode=="test-draft" &&
                item.DocumentTypeCode=="unapproved-invoice-kind" &&
                item.LanguageCode=="unapproved-language" &&
                item.PaperSizeCode=="unapproved-paper-size" &&
                item.Name=="قالب تجريبي" && item.VersionCode=="V1" &&
                item.IsDraft && item.CreatedBy==f.Owner.Subject && item.CreatedAt==now &&
                item.LastModifiedAt==now && item.Revision==1,
                "SYS12 original header/audit fields missing");
            Ensure(f.Store.Count<PrintTemplate>()==0 &&
                f.Store.Count<JournalEntry>()==beforeJournals &&
                f.Store.Count<AuditEvent>()==beforeAudits,
                "SYS12-only draft was persisted or triggered financial effects");
            return Task.CompletedTask;
        });
        test("SYS12-01: invalid tenant, blank catalog fields or version fail closed", f =>
        {
            var now=new FixedClock().UtcNow;
            Error("company_required", () => new PrintTemplate(Guid.Empty, null,
                "test-draft", "doc","lang","paper","name","V1",true,"actor",now));
            Error("print_template_branch_invalid", () => new PrintTemplate(f.CompanyId,Guid.Empty,
                "test-draft", "doc","lang","paper","name","V1",true,"actor",now));
            Error("invalid_text", () => new PrintTemplate(f.CompanyId,null,
                "test-draft", "doc","lang","paper"," ","V1",true,"actor",now));
            Error("invalid_text", () => new PrintTemplate(f.CompanyId,null,
                "test-draft", "", "lang","paper","name","V1",true,"actor",now));
            Error("invalid_text", () => new PrintTemplate(f.CompanyId,null,
                "test-draft", "doc","lang","paper","name","",true,"actor",now));
            Error("invalid_code", () => new PrintTemplate(f.CompanyId,null,
                "test-draft", "doc","lang","paper","name","?",true,"actor",now));
            Ensure(f.Store.Count<PrintTemplate>()==0,
                "SYS12 invalid draft was saved");
            return Task.CompletedTask;
        });
    }
    private static void Error(string expected, Action action)
    {
        try { action(); }
        catch(BusinessException ex) when(ex.Code==expected) { return; }
        throw new Exception("Expected SYS12 "+expected);
    }
    private static void Ensure(bool valid,string message)
    {
        if(!valid) throw new Exception(message);
    }
}
