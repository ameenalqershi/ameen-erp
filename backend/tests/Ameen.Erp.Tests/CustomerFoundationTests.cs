using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
// SAL01 structural fidelity, not approval to sell or extend credit.
public static class CustomerFoundationTests
{
    public static void Register(Action<string,Func<Fixture,Task>> test)
    {
        test("SAL01-FG01..FG07: full customer card field fidelity, no financial effect", f => {
            var now = new FixedClock().UtcNow;
            var category=Guid.NewGuid(); var currency=Guid.NewGuid(); var country=Guid.NewGuid(); var salesperson=Guid.NewGuid();
            var c=new Customer(f.CompanyId,"00023","مشتري","مشتري قانوني",category,f.EquityId,currency,
                1000.123456m,30,country,f.Owner.Subject,now,englishName:"Buyer",notes:"ملاحظة",
                salespersonId:salesperson,phone:"123456",identityType:"local",taxNumber:"001122",
                city:"صنعاء",district:"حي",street:"شارع",buildingNumber:"0004",postalCode:"0020",additionalNumber:"0002");
            Ensure(c.Id!=Guid.Empty && c.CompanyId==f.CompanyId && c.Version!=Guid.Empty &&
                c.Code=="00023" && c.Name=="مشتري" && c.LegalName=="مشتري قانوني" &&
                c.EnglishName=="Buyer" && c.Notes=="ملاحظة" && c.IsActive &&
                c.CustomerCategoryId==category && c.ControlAccountId==f.EquityId &&
                c.TransactionCurrencyId==currency && c.CreditLimit==1000.123456m &&
                c.CreditDays==30 && c.SalespersonId==salesperson && c.Phone=="123456" &&
                c.CountryId==country && c.IdentityType=="local" && c.TaxNumber=="001122" &&
                c.City=="صنعاء" && c.District=="حي" && c.Street=="شارع" &&
                c.BuildingNumber=="0004" && c.PostalCode=="0020" && c.AdditionalNumber=="0002" &&
                c.CreatedBy==f.Owner.Subject && c.Revision==1 &&
                c.CreatedAt==now && c.LastModifiedAt==now,"SAL01 field loss");
            Ensure(f.Store.Count<JournalEntry>()==0 && f.Store.Count<OutboxEvent>()==0,
                "SAL01 construction made financial side effect");
            return Task.CompletedTask;
        });
        test("SAL01-01: invalid references, dates and credit amounts fail", f => {
            var id=Guid.NewGuid(); var now=new FixedClock().UtcNow;
            Customer Make(Guid category,Guid account,Guid currency,Guid country,decimal amount,int days,Guid? sales=null) =>
                new(f.CompanyId,"A","عميل","قانوني",category,account,currency,amount,days,country,f.Owner.Subject,now,salespersonId:sales);
            Reject("customer_reference_required",()=>Make(Guid.Empty,id,id,id,0,0));
            Reject("customer_reference_required",()=>Make(id,Guid.Empty,id,id,0,0));
            Reject("customer_reference_required",()=>Make(id,id,Guid.Empty,id,0,0));
            Reject("customer_reference_required",()=>Make(id,id,id,Guid.Empty,0,0));
            Reject("customer_reference_required",()=>Make(id,id,id,id,0,0,Guid.Empty));
            Reject("invalid_customer_credit_limit",()=>Make(id,id,id,id,-1,0));
            Reject("invalid_customer_credit_limit",()=>Make(id,id,id,id,.0000001m,0));
            Reject("invalid_customer_credit_days",()=>Make(id,id,id,id,0,-1));
            Reject("customer_audit_date_required",()=>new Customer(f.CompanyId,"A","عميل","قانوني",
                id,id,id,0,0,id,f.Owner.Subject,default));
            Ensure(f.Store.Count<JournalEntry>()==0 && f.Store.Count<OutboxEvent>()==0,"Side effect");
            return Task.CompletedTask;
        });
    }
    private static void Reject(string code,Action work) {
        try {work();} catch(BusinessException e) when(e.Code==code) {return;}
        throw new Exception("SAL01 expected "+code);
    }
    private static void Ensure(bool ok,string error) {if(!ok)throw new Exception(error);}
}
