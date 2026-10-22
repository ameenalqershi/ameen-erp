namespace Ameen.Erp.Domain;

public static class Permissions
{
    public const string Read = "ledger.read";
    public const string Configure = "company.configure";
    public const string Members = "members.manage";
    public const string Draft = "journal.write";
    public const string Approve = "journal.approve";
    public const string Post = "journal.post";
    public const string Reverse = "journal.reverse";
    public const string PeriodClose = "period.close";
    public const string PeriodReopen = "period.reopen";
    public const string Audit = "audit.read";
    public const string RecurringManage = "journal.recurring.manage";
    public const string RecurringGenerate = "journal.recurring.generate";
    public const string InventoryWrite = "inventory.opening.write";
    public const string InventoryApprove = "inventory.opening.approve";
    public static readonly string[] All = { Read, Configure, Members, Draft, Approve, Post, Reverse, PeriodClose, PeriodReopen, Audit, RecurringManage, RecurringGenerate, InventoryWrite, InventoryApprove };
}
