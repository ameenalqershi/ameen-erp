using Ameen.Erp.Application;

namespace Ameen.Erp.Tests;

// Test convenience only: each property is an actual independent application service.
public sealed class TestServices
{
    public AccountService Account { get; }
    public AuditService Audit { get; }
    public BranchService Branch { get; }
    public CompanyService Company { get; }
    public CostCenterService CostCenter { get; }
    public CurrencyService Currency { get; }
    public ExchangeRateService ExchangeRate { get; }
    public FiscalCalendarService FiscalCalendar { get; }
    public ItemGroupService ItemGroup { get; }
    public ItemService Item { get; }
    public JournalService Journal { get; }
    public MembershipService Membership { get; }
    public PeriodCloseService PeriodClose { get; }
    public RecurringJournalService RecurringJournal { get; }
    public ReportService Report { get; }
    public SetupQueryService SetupQuery { get; }
    public UnitService Unit { get; }
    public WarehouseService Warehouse { get; }
    public TestServices(IErpStore store, ITransactionRunner transactions, IClock clock)
    {
        Currency = new CurrencyService(store, transactions, clock);
        Account = new AccountService(store, transactions, clock, Currency);
        Audit = new AuditService(store, transactions, clock);
        Branch = new BranchService(store, transactions, clock);
        Company = new CompanyService(store, transactions, clock, Currency);
        CostCenter = new CostCenterService(store, transactions, clock, Branch);
        ExchangeRate = new ExchangeRateService(store, transactions, clock, Currency);
        FiscalCalendar = new FiscalCalendarService(store, transactions, clock);
        ItemGroup = new ItemGroupService(store, transactions, clock);
        Item = new ItemService(store, transactions, clock, ItemGroup);
        Journal = new JournalService(store, transactions, clock, Branch, Currency);
        Membership = new MembershipService(store, transactions, clock, Branch);
        PeriodClose = new PeriodCloseService(store, transactions, clock);
        RecurringJournal = new RecurringJournalService(store, transactions, clock, Branch, Currency, ExchangeRate, Journal);
        Report = new ReportService(store, transactions, clock);
        SetupQuery = new SetupQueryService(store, transactions, clock);
        Unit = new UnitService(store, transactions, clock);
        Warehouse = new WarehouseService(store, transactions, clock);
    }
}
