using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;

public static class CustomerGroupFoundationTests
{
    public static void Register(Action<string,Func<Fixture,Task>> test)
    {
        test("SAL15-01: group field fidelity without invoice or ledger effect", f => {
            var now=new FixedClock().UtcNow;
            var price=Guid.NewGuid();var currency=Guid.NewGuid();var term=Guid.NewGuid();var parent=Guid.NewGuid();
            var g=new CustomerGroup(f.CompanyId,"00015","مجموعة",price,1000.123456m,currency,term,
                f.EquityId,f.Owner.Subject,now,englishName:"Group",notes:"ملاحظة",parentGroupId:parent);
            Ensure(g.CompanyId==f.CompanyId && g.Id!=Guid.Empty && g.Version!=Guid.Empty &&
                g.Code=="00015" && g.Name=="مجموعة" && g.EnglishName=="Group" &&
                g.IsActive && g.Notes=="ملاحظة" && g.ParentGroupId==parent &&
                g.PriceListId==price && g.DefaultCreditLimit==1000.123456m &&
                g.CreditCurrencyId==currency && g.PaymentTermId==term &&
                g.ControlAccountId==f.EquityId && g.CreatedBy==f.Owner.Subject &&
                g.CreatedAt==now && g.LastModifiedAt==now && g.Revision==1,"SAL15 field loss");
            Ensure(f.Store.Count<Customer>()==0 && f.Store.Count<JournalEntry>()==0 &&
                f.Store.Count<OutboxEvent>()==0,"SAL15 unauthorized financial effect");
            return Task.CompletedTask;
        });
        test("SAL15-01: invalid required references, amount and audit rejected", f => {
            var id=Guid.NewGuid();var now=new FixedClock().UtcNow;
            CustomerGroup Make(Guid price,Guid currency,Guid term,Guid account,decimal amount,Guid? parent=null) =>
                new(f.CompanyId,"A","مجموعة",price,amount,currency,term,account,f.Owner.Subject,now,parentGroupId:parent);
            Reject("customer_group_reference_required",()=>Make(Guid.Empty,id,id,id,0));
            Reject("customer_group_reference_required",()=>Make(id,Guid.Empty,id,id,0));
            Reject("customer_group_reference_required",()=>Make(id,id,Guid.Empty,id,0));
            Reject("customer_group_reference_required",()=>Make(id,id,id,Guid.Empty,0));
            Reject("customer_group_reference_required",()=>Make(id,id,id,id,0,Guid.Empty));
            Reject("invalid_customer_group_credit_limit",()=>Make(id,id,id,id,-1));
            Reject("invalid_customer_group_credit_limit",()=>Make(id,id,id,id,.0000001m));
            Reject("customer_group_audit_date_required",()=>new CustomerGroup(f.CompanyId,"A","مجموعة",id,0,id,id,id,f.Owner.Subject,default));
            return Task.CompletedTask;
        });
    }
    private static void Reject(string code,Action work) {
        try {work();} catch(BusinessException e) when(e.Code==code) {return;}
        throw new Exception("SAL15 expected "+code);
    }
    private static void Ensure(bool ok,string error) {if(!ok)throw new Exception(error);}
}
