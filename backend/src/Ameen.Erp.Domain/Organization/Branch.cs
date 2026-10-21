namespace Ameen.Erp.Domain;

public sealed class Branch : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public string? City { get; private set; }
    public string? Address { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid? DefaultCostCenterId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    public bool IsProfileComplete => City is not null && Address is not null;
    private Branch() { }
    public Branch(Guid companyId, string code, string name, string city, string address,
        Guid? parentId = null, Guid? defaultCostCenterId = null, string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, city, address, parentId, defaultCostCenterId, englishName, notes);
    public void Configure(string code, string name, string city, string address, Guid? parentId,
        Guid? defaultCostCenterId, string? englishName, string? notes)
    {
        Code = Check.Code(code, "رمز الفرع"); Name = Check.Text(name, "اسم الفرع");
        City = Check.Text(city, "المدينة"); Address = Check.Text(address, "العنوان", 500);
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        ParentId = parentId; DefaultCostCenterId = defaultCostCenterId; Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "branch_revision_exhausted", "بلغ إصدار الفرع الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
