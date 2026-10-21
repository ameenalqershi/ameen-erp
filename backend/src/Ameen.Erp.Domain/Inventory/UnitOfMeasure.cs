namespace Ameen.Erp.Domain;

public sealed class UnitOfMeasure : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string StandardCode { get; private set; } = "";
    public int QuantityScale { get; private set; }
    public string Description { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    private UnitOfMeasure() { }
    public UnitOfMeasure(Guid companyId, string code, string name, string standardCode, int quantityScale, string description,
        string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, standardCode, quantityScale, description, englishName, notes);
    public void Configure(string code, string name, string standardCode, int quantityScale, string description, string? englishName, string? notes)
    {
        Check.That(quantityScale is >= 0 and <= 6, "invalid_quantity_scale", "دقة الكمية بين صفر وست خانات.");
        Code = Check.Code(code, "رمز الوحدة"); Name = Check.Text(name, "اسم الوحدة");
        StandardCode = Check.Code(standardCode, "رمز الوحدة القياسي", 32); QuantityScale = quantityScale;
        Description = Check.Text(description, "وصف الوحدة", 500);
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000); Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "unit_revision_exhausted", "بلغ إصدار الوحدة الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
