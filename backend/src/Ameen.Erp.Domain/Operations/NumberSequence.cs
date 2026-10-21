namespace Ameen.Erp.Domain;

public sealed class NumberSequence : CompanyEntity
{
    public string Scope { get; private set; } = "";
    public long LastNumber { get; private set; }
    private NumberSequence() { }
    public NumberSequence(Guid companyId, string scope) : base(companyId) { Scope = scope; }
    public long Next() { LastNumber = checked(LastNumber + 1); Touch(); return LastNumber; }
}
