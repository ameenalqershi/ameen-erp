namespace Ameen.Erp.Domain;
public sealed class InventoryOpeningPostingSettings : CompanyEntity
{
    public Guid ClearingAccountId { get; private set; }
    private InventoryOpeningPostingSettings() { }
    public InventoryOpeningPostingSettings(Guid cid, Guid accountId) : base(cid) => Configure(accountId);
    public void Configure(Guid accountId)
    {
        Check.That(accountId != Guid.Empty, "opening_clearing_account_required", "حساب تسوية الافتتاح مطلوب.");
        ClearingAccountId = accountId; Touch();
    }
}
