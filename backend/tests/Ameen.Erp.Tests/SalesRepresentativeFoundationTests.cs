using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;

public static class SalesRepresentativeFoundationTests
{
    public static void Register(Action<string,Func<Fixture,Task>> test)
    {
        test("SAL09-01: representative field fidelity and no customer access side effect", f => {
            var now=new FixedClock().UtcNow;
            var employee=Guid.NewGuid(); var supervisor=Guid.NewGuid();
            var custody=Guid.NewGuid(); var commission=Guid.NewGuid();
            var rep=new SalesRepresentative(f.CompanyId,"00009","مندوب","صنعاء",f.Owner.Subject,now,
                englishName:"Representative",notes:"ملاحظات",employeeId:employee,
                supervisorId:supervisor,custodyAccountId:custody,commissionPolicyId:commission);
            Ensure(rep.CompanyId==f.CompanyId && rep.Id!=Guid.Empty && rep.Version!=Guid.Empty &&
                rep.Code=="00009" && rep.Name=="مندوب" && rep.EnglishName=="Representative" &&
                rep.IsActive && rep.Notes=="ملاحظات" && rep.Region=="صنعاء" &&
                rep.EmployeeId==employee && rep.SupervisorId==supervisor &&
                rep.CustodyAccountId==custody && rep.CommissionPolicyId==commission &&
                rep.CreatedBy==f.Owner.Subject && rep.CreatedAt==now &&
                rep.LastModifiedAt==now && rep.Revision==1,"SAL09 field loss");
            Ensure(f.Store.Count<Customer>()==0 && f.Store.Count<JournalEntry>()==0 &&
                f.Store.Count<OutboxEvent>()==0,"SAL09 created unauthorized side effects");
            return Task.CompletedTask;
        });
        test("SAL09-01: reject missing required fields and invalid optional references", f => {
            var now=new FixedClock().UtcNow;
            Reject("sales_rep_audit_date_required",()=>new SalesRepresentative(f.CompanyId,"R","مندوب","منطقة",f.Owner.Subject,default));
            Reject("sales_rep_invalid_reference",()=>new SalesRepresentative(f.CompanyId,"R","مندوب","منطقة",f.Owner.Subject,now,employeeId:Guid.Empty));
            Reject("sales_rep_invalid_reference",()=>new SalesRepresentative(f.CompanyId,"R","مندوب","منطقة",f.Owner.Subject,now,supervisorId:Guid.Empty));
            Reject("sales_rep_invalid_reference",()=>new SalesRepresentative(f.CompanyId,"R","مندوب","منطقة",f.Owner.Subject,now,custodyAccountId:Guid.Empty));
            Reject("sales_rep_invalid_reference",()=>new SalesRepresentative(f.CompanyId,"R","مندوب","منطقة",f.Owner.Subject,now,commissionPolicyId:Guid.Empty));
            return Task.CompletedTask;
        });
    }
    private static void Reject(string code,Action work) {
        try {work();} catch(BusinessException e) when(e.Code==code) {return;}
        throw new Exception("SAL09 expected "+code);
    }
    private static void Ensure(bool ok,string error) {if(!ok)throw new Exception(error);}
}
