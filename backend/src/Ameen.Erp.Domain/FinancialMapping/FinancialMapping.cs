namespace Ameen.Erp.Domain;
public sealed class FinancialMapping : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string Name { get; private set; } = "";
    public FinancialStatementKind StatementKind { get; private set; }
    public string VersionCode { get; private set; } = "";
    public DateOnly EffectiveFrom { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<FinancialMappingLine> Lines { get; private set; } = new();
    private FinancialMapping() { }
    public FinancialMapping(Guid cid,Guid? branch,string name,FinancialStatementKind kind,string version,DateOnly effective,string actor,DateTimeOffset now):base(cid)
    {Check.That(branch!=Guid.Empty,"invalid_mapping_scope","نطاق غير صالح.");BranchId=branch;CreatedBy=Check.Text(actor,"المنشئ");CreatedAt=now;LastModifiedAt=now;Configure(name,kind,version,effective);}
    public void Configure(string name,FinancialStatementKind kind,string version,DateOnly effective)
    {
        Check.That(Enum.IsDefined(kind),"invalid_statement_kind","نوع قائمة غير صالح.");
        Check.That(effective!=DateOnly.MinValue,"mapping_date_required","تاريخ سريان الربط مطلوب.");
        Name=Check.Text(name,"اسم النموذج");StatementKind=kind;VersionCode=Check.Code(version,"إصدار النموذج");EffectiveFrom=effective;Touch();
    }
    public void SetLines(IEnumerable<FinancialMappingLine> source)
    {
        var lines=source.ToList();Check.That(lines.Count is >=1 and <=500,"invalid_mapping_lines","النموذج يتكون من 1 إلى 500 بند.");
        Check.That(lines.All(x=>x.CompanyId==CompanyId && x.MappingId==Id),"mapping_line_scope","بند خارج نطاق النموذج.");
        Check.That(lines.Select(x=>x.Code).Distinct(StringComparer.Ordinal).Count()==lines.Count,"duplicate_mapping_code","رمز بند مكرر.");
        Lines=lines;Touch();
    }
    public void Advance(DateTimeOffset now)
    {Check.That(Revision<int.MaxValue && now>=CreatedAt,"invalid_mapping_revision","إصدار أو وقت تعديل غير صالح.");Revision++;LastModifiedAt=now;Touch();}
    public void SetActive(bool active,DateTimeOffset now){Advance(now);IsActive=active;}
}
