namespace Ameen.Erp.Domain;

public sealed class Currency : CompanyEntity
{
    public string Code { get; private set; } = "";
    public int Scale { get; private set; }
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public string? DisplaySymbol { get; private set; }
    public string? FractionName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    private Currency() { }
    public Currency(Guid companyId, string code, int scale, string name, string? englishName = null,
        string? notes = null, string? displaySymbol = null, string? fractionName = null) : base(companyId)
    {
        Code = Check.Currency(code);
        Configure(scale, name, englishName, notes, displaySymbol, fractionName);
    }
    public void Configure(int scale, string name, string? englishName, string? notes, string? displaySymbol, string? fractionName)
    {
        Check.That(scale is >= 0 and <= 6, "invalid_scale", "دقة العملة بين صفر وست خانات.");
        Scale = scale; Name = Check.Text(name, "اسم العملة");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        DisplaySymbol = displaySymbol is null ? null : Check.Text(displaySymbol, "رمز العرض", 20);
        FractionName = fractionName is null ? null : Check.Text(fractionName, "اسم الجزء", 100);
        Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "currency_revision_exhausted", "بلغ إصدار العملة الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
