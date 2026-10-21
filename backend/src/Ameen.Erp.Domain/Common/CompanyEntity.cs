namespace Ameen.Erp.Domain;

public abstract class CompanyEntity : Entity
{
    public Guid CompanyId { get; protected set; }
    protected CompanyEntity() { }
    protected CompanyEntity(Guid companyId)
    {
        Check.That(companyId != Guid.Empty, "company_required", "الشركة مطلوبة.");
        CompanyId = companyId;
    }
}
