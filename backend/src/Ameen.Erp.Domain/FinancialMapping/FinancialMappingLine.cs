namespace Ameen.Erp.Domain;
public sealed class FinancialMappingLine : CompanyEntity
{
    public Guid MappingId { get; private set; }
    public int Position { get; private set; }
    public string Code { get; private set; } = "";
    public string? ParentCode { get; private set; }
    public Guid? ParentLineId { get; private set; }
    public string Title { get; private set; } = "";
    public Guid? AccountId { get; private set; }
    public FinancialDisplaySign DisplaySign { get; private set; }
    public string? Formula { get; private set; }
    private FinancialMappingLine() { }
    public FinancialMappingLine(Guid cid,Guid mid,int position,string code,string? parent,string title,Guid? account,
        FinancialDisplaySign sign,string? formula):base(cid)
    {
        MappingId=mid;Position=position;Code=Check.Code(code,"رمز البند");ParentCode=string.IsNullOrWhiteSpace(parent)?null:Check.Code(parent,"رمز الأب");
        Title=Check.Text(title,"عنوان البند");Check.That(account!=Guid.Empty,"invalid_mapping_account","مرجع حساب غير صالح.");AccountId=account;
        Check.That(Enum.IsDefined(sign),"invalid_display_sign","إشارة عرض غير صالحة.");DisplaySign=sign;
        Formula=string.IsNullOrWhiteSpace(formula)?null:Check.Text(formula,"المعادلة",2000);
        Check.That(AccountId is null || Formula is null,"ambiguous_mapping_line","البند حساب/مجموعة أو معادلة أو عنوان دون مبلغ.");
    }
    public void LinkParent(Guid? id){Check.That(id!=Id,"mapping_parent_cycle","البند لا يكون أبًا لنفسه.");ParentLineId=id;}
}
