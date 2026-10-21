using System.Linq.Expressions;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record Actor(string Subject, bool IsPlatformAdministrator = false);
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

// All tenant reads require a company key; there is no unscoped generic query.
public interface IErpStore
{
    Task<Company?> GetCompany(Guid id, CancellationToken ct);
    Task<bool> CompanyCodeExists(string code, CancellationToken ct);
    Task<IReadOnlyList<Company>> CompaniesFor(string subject, CancellationToken ct);
    Task<T?> Get<T>(Guid companyId, Guid id, CancellationToken ct) where T : CompanyEntity;
    Task<IReadOnlyList<T>> Find<T>(Guid companyId, Expression<Func<T, bool>> predicate, CancellationToken ct) where T : CompanyEntity;
    Task<Page<T>> ReadPage<T>(Guid companyId, Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> order, int offset, int limit, CancellationToken ct) where T : CompanyEntity;
    void Add(Entity entity);
    void RemoveLines(IEnumerable<JournalLine> lines);
    void RemoveWarehouseLocations(IEnumerable<WarehouseLocation> locations);
    void RemoveItemUnits(IEnumerable<ItemUnit> units);
    void RemoveRecurringLines(IEnumerable<RecurringJournalLine> lines);
    Task Save(CancellationToken ct);
}

public sealed record CommandReceipt(Guid ResourceId, Guid Version, string Status, string? Number = null);
public sealed record CommandIdentity(Guid CompanyId, string Key, string Operation, string ActorId, string Fingerprint);
public interface ITransactionRunner
{
    Task<CommandReceipt> Run(CommandIdentity identity, Func<CancellationToken, Task> authorize,
        Func<CancellationToken, Task<CommandReceipt>> work, CancellationToken ct);
}

public sealed record CreateCompanyRequest(Guid Id, string Code, string Name, string CountryCode, string FunctionalCurrency, int CurrencyScale, CompanyProfile Profile);
public sealed record EditCompanyRequest(Guid ExpectedVersion, string Code, string Name, string CountryCode, string FunctionalCurrency, CompanyProfile Profile, string Reason);
public sealed record SetCompanyStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record CompanyUsage(Guid CompanyId, int BranchCount, int AccountCount, int CurrencyCount, int FiscalYearCount,
    int JournalCount, int RecurringTemplateCount, int ExchangeRateCount, bool CanChangeFunctionalCurrency);
public sealed record CreateBranchRequest(string Code, string Name, string City, string Address,
    Guid? ParentId = null, Guid? DefaultCostCenterId = null, string? EnglishName = null, string? Notes = null);
public sealed record EditBranchRequest(Guid ExpectedVersion, string Code, string Name, string City, string Address,
    Guid? ParentId, Guid? DefaultCostCenterId, string? EnglishName, string? Notes, string Reason);
public sealed record SetBranchStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record BranchUsage(Guid BranchId, int ChildCount, int CostCenterCount, int MembershipCount,
    int JournalCount, int RecurringTemplateCount, bool HasSubtreeFinancialUsage);
public sealed record CreateCurrencyRequest(string Code, int Scale, string Name, string? EnglishName = null,
    string? Notes = null, string? DisplaySymbol = null, string? FractionName = null);
public sealed record EditCurrencyRequest(Guid ExpectedVersion, int Scale, string Name, string? EnglishName,
    string? Notes, string? DisplaySymbol, string? FractionName, string Reason);
public sealed record SetCurrencyStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record CurrencyUsage(Guid CurrencyId, bool IsFunctionalCurrency, int AccountCount, int ExchangeRateCount,
    int JournalCount, int RecurringTemplateCount);
public sealed record ExchangeRateDetail(ExchangeRate Rate, string FunctionalCurrency);
public sealed record ExchangeRateQuote(Guid CompanyId, string CurrencyCode, string FunctionalCurrency, DateOnly AccountingDate,
    Guid? ExchangeRateId, DateOnly? EffectiveDate, decimal RateToFunctional, string? Source, bool IsFunctionalCurrency);
public sealed record PeriodCloseCheck(string Code, bool Passed);
public sealed record PeriodCloseIssue(Guid JournalId, Guid BranchId, string Number, JournalState State, string Code);
public sealed record PeriodClosePreview(Guid PeriodId, Guid ExpectedVersion, DateTimeOffset CheckedAt, bool GeneralLedgerOnly,
    bool CanCloseGeneralLedger, IReadOnlyList<PeriodCloseCheck> Checks, int PostedJournalCount, int UnpostedJournalCount, Page<PeriodCloseIssue> Issues);
public sealed record CreateExchangeRateRequest(string CurrencyCode, DateOnly EffectiveDate, decimal RateToFunctional, string Source);
public sealed record FiscalPeriodRequest(string Code, DateOnly StartsOn, DateOnly EndsOn);
public sealed record CreateFiscalYearRequest(string Code, DateOnly StartsOn, DateOnly EndsOn, FiscalPeriodRequest[]? Periods = null);
public enum FiscalCalendarState { Open, PartiallyClosed, Closed }
public sealed record FiscalCalendar(FiscalYear Year, IReadOnlyList<FiscalPeriod> Periods, FiscalCalendarState State, bool GeneralLedgerOnly);
public sealed record FiscalYearUsage(Guid FiscalYearId, int JournalCount, int UnpostedJournalCount, int RecurringGenerationCount);
public sealed record CreateCostCenterRequest(string Code, string Name, Guid? BranchId, Guid? ParentId = null,
    Guid? ResponsibleMembershipId = null, string? EnglishName = null, string? Notes = null);
public sealed record EditCostCenterRequest(Guid ExpectedVersion, string Code, string Name, Guid? BranchId,
    Guid? ParentId, Guid? ResponsibleMembershipId, string? EnglishName, string? Notes, string Reason);
public sealed record SetCostCenterStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record CostCenterUsage(Guid CostCenterId, int ChildCenterCount, int JournalCount, int RecurringTemplateCount,
    bool HasUsageInSubtree, Page<AccountUsageReference> References);
public sealed record GrantMembershipRequest(string Subject, string[] Permissions, bool AllBranches, Guid[] BranchIds, Guid? ExpectedVersion);
public sealed record SetMembershipStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record CreateAccountRequest(string Code, string Name, AccountType Type, Guid? ParentId,
    bool IsPosting, bool IsControl, bool RequiresCostCenter, string? CurrencyCode, NormalBalance NormalBalance,
    string? EnglishName = null, string? Notes = null);
public sealed record EditAccountRequest(Guid ExpectedVersion, string Code, string Name, AccountType Type, Guid? ParentId,
    bool IsPosting, bool IsControl, bool RequiresCostCenter, string? CurrencyCode, NormalBalance NormalBalance,
    string? EnglishName, string? Notes, string Reason);
public sealed record AccountUsageReference(string Kind, Guid ResourceId, Guid BranchId, string NumberOrName, string State);
public sealed record AccountUsage(Guid AccountId, int ChildAccountCount, int JournalCount, int JournalLineCount,
    int RecurringTemplateCount, bool HasUsageInSubtree, Page<AccountUsageReference> References);
public sealed record SetAccountStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record ChangePeriodRequest(Guid ExpectedVersion, string Reason);
public sealed record JournalLineRequest(Guid AccountId, Guid? CostCenterId, string Description,
    string CurrencyCode, Guid? ExchangeRateId, decimal Debit, decimal Credit);
public sealed record CreateJournalRequest(Guid BranchId, DateOnly DocumentDate, DateOnly AccountingDate,
    string Description, string? ExternalReference, JournalLineRequest[] Lines);
public sealed record EditJournalRequest(Guid ExpectedVersion, string Description, JournalLineRequest[] Lines);
public sealed record TransitionRequest(Guid ExpectedVersion, string? Reason = null);
public sealed record ReverseJournalRequest(Guid ExpectedVersion, DateOnly AccountingDate, string Reason);
public sealed record RecurringLineRequest(Guid AccountId, Guid? CostCenterId, string Description, string CurrencyCode, decimal Debit, decimal Credit);
public sealed record CreateRecurringJournalRequest(Guid BranchId, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly EndsOn, RecurringLineRequest[] Lines);
public sealed record EditRecurringJournalRequest(Guid ExpectedVersion, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly EndsOn, RecurringLineRequest[] Lines);
public sealed record SetRecurringStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record GenerateRecurringJournalRequest(Guid ExpectedVersion, Guid FiscalPeriodId);

public sealed record Page<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);
public sealed record JournalSummary(Guid Id, Guid Version, string Number, Guid BranchId, DateOnly AccountingDate,
    string Description, JournalState State, decimal Debit, decimal Credit, JournalSource Source, Guid? RecurringTemplateId);
public sealed record RecurringJournalSummary(Guid Id, Guid Version, Guid BranchId, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly EndsOn, bool IsActive, int LineCount);
public sealed record RecurringScheduleRow(Guid FiscalPeriodId, string PeriodCode, DateOnly DueDate, bool IsPeriodClosed,
    Guid? GeneratedJournalId, JournalState? GeneratedState);
public sealed record TrialBalanceRow(Guid AccountId, string Code, string Name, decimal OpeningDebit,
    decimal OpeningCredit, decimal PeriodDebit, decimal PeriodCredit, decimal ClosingDebit, decimal ClosingCredit,
    Guid? ParentId, int Level, bool IsPosting);
public sealed record TrialBalanceResult(Guid CompanyId, string Currency, DateOnly From, DateOnly To,
    IReadOnlyList<Guid> BranchIds, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalOpeningDebit,
    decimal TotalOpeningCredit, decimal TotalPeriodDebit, decimal TotalPeriodCredit, decimal TotalClosingDebit, decimal TotalClosingCredit,
    Guid? CostCenterId, int? AccountLevel, bool ShowZero);
public sealed record LedgerRow(Guid JournalId, string Number, DateOnly AccountingDate, int Position,
    string Description, decimal Debit, decimal Credit, decimal RunningBalance, string CurrencyCode,
    decimal OriginalDebit, decimal OriginalCredit, decimal ExchangeRate);
public sealed record AccountLedgerResult(Guid AccountId, string Currency, DateOnly From, DateOnly To,
    decimal OpeningBalance, decimal ClosingBalance, decimal PeriodDebit, decimal PeriodCredit, Page<LedgerRow> Entries,
    Guid CompanyId, IReadOnlyList<Guid> BranchIds, Guid? CostCenterId, string? ExternalReference);

public sealed record CreateUnitRequest(string Code, string Name, string StandardCode, int QuantityScale, string Description, string? EnglishName = null, string? Notes = null);
public sealed record EditUnitRequest(Guid ExpectedVersion, string Code, string Name, string StandardCode, int QuantityScale, string Description, string? EnglishName, string? Notes, string Reason);
public sealed record SetUnitStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);

public sealed record CreateItemGroupRequest(string Code, string Name, Guid SalesAccountId, Guid? ParentId = null,
    Guid? InventoryAccountId = null, Guid? CostAccountId = null, string? EnglishName = null, string? Notes = null);
public sealed record EditItemGroupRequest(Guid ExpectedVersion, string Code, string Name, Guid SalesAccountId, Guid? ParentId,
    Guid? InventoryAccountId, Guid? CostAccountId, string? EnglishName, string? Notes, string Reason);
public sealed record SetItemGroupStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);

public sealed record ItemUnitRequest(Guid UnitId, decimal FactorToBase, string? Barcode = null, decimal? SalePrice = null);
public sealed record CreateItemRequest(string Code, string Name, ItemType Type, ItemTracking Tracking, Guid GroupId, Guid BaseUnitId,
    ItemUnitRequest[] Units, int? ShelfLifeDays = null, decimal? ReorderLevel = null, string? EnglishName = null, string? Notes = null);
public sealed record EditItemRequest(Guid ExpectedVersion, string Code, string Name, ItemType Type, ItemTracking Tracking, Guid GroupId, Guid BaseUnitId,
    ItemUnitRequest[] Units, int? ShelfLifeDays, decimal? ReorderLevel, string? EnglishName, string? Notes, string Reason);
public sealed record SetItemStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
public sealed record ItemQuantityConversion(Guid ItemId, Guid ItemVersion, Guid UnitId, Guid BaseUnitId, decimal Quantity, decimal FactorToBase, decimal BaseQuantity);

public sealed record ItemGroupUsage(Guid GroupId, Guid GroupVersion, int DirectChildGroups, int ActiveDirectChildGroups,
    int DescendantGroups, int DirectItems, int ActiveDirectItems, int SubtreeItems, int ActiveSubtreeItems,
    bool StructureLocked, bool HasDeactivationBlockers);

public sealed record WarehouseLocationRequest(string Code, string Description);
public sealed record CreateWarehouseRequest(string Code, string Name, Guid BranchId, Guid KeeperMembershipId, Guid InventoryAccountId,
    WarehouseType Type, bool TrackLocations, WarehouseLocationRequest[] Locations, string? EnglishName = null, string? Notes = null);
public sealed record EditWarehouseRequest(Guid ExpectedVersion, string Code, string Name, Guid BranchId, Guid KeeperMembershipId, Guid InventoryAccountId,
    WarehouseType Type, bool TrackLocations, WarehouseLocationRequest[] Locations, string? EnglishName, string? Notes, string Reason);
public sealed record SetWarehouseStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
