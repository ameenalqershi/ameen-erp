namespace Ameen.Erp.Domain;

public sealed class CostCenter : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? ResponsibleMembershipId { get; private set; }
    public int Revision { get; private set; }
    public bool IsActive { get; private set; } = true;
    private CostCenter() { }
    public CostCenter(Guid companyId, string code, string name, Guid? branchId, Guid? parentId = null,
        Guid? responsibleMembershipId = null, string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, branchId, parentId, responsibleMembershipId, englishName, notes);
    public void Configure(string code, string name, Guid? branchId, Guid? parentId,
        Guid? responsibleMembershipId, string? englishName, string? notes)
    {
        Code = Check.Code(code, "رمز المركز"); Name = Check.Text(name, "اسم المركز"); BranchId = branchId;
        ParentId = parentId; ResponsibleMembershipId = responsibleMembershipId;
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000); Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "center_revision_exhausted", "بلغ إصدار المركز الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
