using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.AspNetCore.Mvc;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

public static class Endpoints
{
    public static void MapErp(this WebApplication app)
    {
        // Every ERP route is authenticated. Company and branch membership is enforced again by the application.
        app.MapGet("/api/v1/companies", async (ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCompanies(Actor(h), ct))).Produces<IReadOnlyList<Company>>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapPost("/api/v1/companies", async (CreateCompanyRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCompany(Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS01 Companies");
        const string p = "/api/v1/companies/{companyId:guid}";
        app.MapGet(p, async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCompany(companyId, Actor(h), ct))).Produces<Company>().RequireAuthorization().WithTags("SYS01 Companies");

        app.MapPut(p, async (Guid companyId, EditCompanyRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCompany(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapPost(p + "/status", async (Guid companyId, SetCompanyStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCompanyStatus(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapGet(p + "/usage", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCompanyUsage(companyId, Actor(h), ct))).Produces<CompanyUsage>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapGet(p + "/profile-audit-events", async (Guid companyId, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCompanyAudit(companyId, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS01 Companies");

        app.MapGet(p + "/branches", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListBranches(companyId, Actor(h), ct))).Produces<IReadOnlyList<Branch>>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapPost(p + "/branches", async (Guid companyId, CreateBranchRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateBranch(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetBranch(companyId, id, Actor(h), ct))).Produces<Branch>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapPut(p + "/branches/{id:guid}", async (Guid companyId, Guid id, EditBranchRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditBranch(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapPost(p + "/branches/{id:guid}/status", async (Guid companyId, Guid id, SetBranchStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetBranchStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/{id:guid}/usage", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetBranchUsage(companyId, id, Actor(h), ct))).Produces<BranchUsage>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListBranchAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/search", async (Guid companyId, string? search, Guid? parentId, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchBranches(companyId, Actor(h), search, parentId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Branch>>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/currencies", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<Currency>(companyId, Actor(h), ct))).Produces<IReadOnlyList<Currency>>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapPost(p + "/currencies", async (Guid companyId, CreateCurrencyRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCurrency(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/search", async (Guid companyId, string? search, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchCurrencies(companyId, Actor(h), search, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Currency>>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCurrency(companyId, id, Actor(h), ct))).Produces<Currency>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapPut(p + "/currencies/{id:guid}", async (Guid companyId, Guid id, EditCurrencyRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCurrency(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapPost(p + "/currencies/{id:guid}/status", async (Guid companyId, Guid id, SetCurrencyStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCurrencyStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/{id:guid}/usage", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCurrencyUsage(companyId, id, Actor(h), ct))).Produces<CurrencyUsage>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCurrencyAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/exchange-rates", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<ExchangeRate>(companyId, Actor(h), ct))).Produces<IReadOnlyList<ExchangeRate>>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapPost(p + "/exchange-rates", async (Guid companyId, CreateExchangeRateRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateExchangeRate(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapGet(p + "/fiscal-years", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<FiscalYear>(companyId, Actor(h), ct))).Produces<IReadOnlyList<FiscalYear>>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapPost(p + "/fiscal-years", async (Guid companyId, CreateFiscalYearRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateFiscalYear(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapGet(p + "/periods", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<FiscalPeriod>(companyId, Actor(h), ct))).Produces<IReadOnlyList<FiscalPeriod>>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapPost(p + "/periods/{id:guid}/close", async (Guid companyId, Guid id, ChangePeriodRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ChangePeriod(companyId, id, Actor(h), Key(h), r, true, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL11 Period close");
        app.MapPost(p + "/periods/{id:guid}/reopen", async (Guid companyId, Guid id, ChangePeriodRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ChangePeriod(companyId, id, Actor(h), Key(h), r, false, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL12 Period reopen");
        app.MapGet(p + "/cost-centers", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<CostCenter>(companyId, Actor(h), ct))).Produces<IReadOnlyList<CostCenter>>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapPost(p + "/cost-centers", async (Guid companyId, CreateCostCenterRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCostCenter(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/search", async (Guid companyId, string? search, Guid? branchId, Guid? parentId, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchCostCenters(companyId, Actor(h), search, branchId, parentId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<CostCenter>>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCostCenter(companyId, id, Actor(h), ct))).Produces<CostCenter>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapPut(p + "/cost-centers/{id:guid}", async (Guid companyId, Guid id, EditCostCenterRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCostCenter(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapPost(p + "/cost-centers/{id:guid}/status", async (Guid companyId, Guid id, SetCostCenterStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCostCenterStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/{id:guid}/usage", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCostCenterUsage(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<CostCenterUsage>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCostCenterAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/memberships", async (Guid companyId, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListMembers(companyId, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<Membership>>().RequireAuthorization().WithTags("SYS07 SYS08 Memberships");
        app.MapPost(p + "/memberships", async (Guid companyId, GrantMembershipRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GrantMembership(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS07 SYS08 Memberships");
        app.MapPost(p + "/memberships/{id:guid}/status", async (Guid companyId, Guid id, SetMembershipStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetMembershipStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS06 Membership status");
        app.MapGet(p + "/accounts", async (Guid companyId, string? search, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAccounts(companyId, Actor(h), search, offset ?? 0, limit ?? 50, ct))).Produces<Page<Account>>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapPost(p + "/accounts", async (Guid companyId, CreateAccountRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateAccount(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/accounts/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetAccount(companyId, id, Actor(h), ct))).Produces<Account>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapPut(p + "/accounts/{id:guid}", async (Guid companyId, Guid id, EditAccountRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditAccount(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/accounts/{id:guid}/usage", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetAccountUsage(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<AccountUsage>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/accounts/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAccountAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapPost(p + "/accounts/{id:guid}/status", async (Guid companyId, Guid id, SetAccountStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetAccountStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/journals", async (Guid companyId, Guid? branchId, JournalState? state, JournalSource? source, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListJournals(companyId, Actor(h), branchId, state, offset ?? 0, limit ?? 50, ct, source))).Produces<Page<JournalSummary>>().RequireAuthorization().WithTags("GL03 Journals");
        app.MapGet(p + "/journals/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetJournal(companyId, id, Actor(h), ct))).Produces<JournalEntry>().RequireAuthorization().WithTags("GL03 Journals");
        app.MapPost(p + "/journals", async (Guid companyId, CreateJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateJournal(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL03 Journals");
        app.MapPut(p + "/journals/{id:guid}", async (Guid companyId, Guid id, EditJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL03 Journals");
        foreach (var transition in new[] { "submit", "approve", "return", "post", "cancel" })
        {
            var action = transition;
            app.MapPost(p + "/journals/{id:guid}/" + action,
                async (Guid companyId, Guid id, TransitionRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
                    Results.Ok(await s.TransitionJournal(companyId, id, Actor(h), Key(h), r, action, ct)))
                .Produces<CommandReceipt>().RequireAuthorization().WithTags("GL05 GL06 Journal workflow");
        }
        app.MapPost(p + "/journals/{id:guid}/reverse", async (Guid companyId, Guid id, ReverseJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ReverseJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL07 Reversals");
        app.MapGet(p + "/reports/trial-balance", async (Guid companyId, [FromQuery] string from, [FromQuery] string to,
            Guid? branchId, Guid? costCenterId, int? accountLevel, bool? showZero, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.TrialBalance(companyId, Actor(h), Date(from), Date(to), branchId, ct, costCenterId, accountLevel, showZero ?? true)))
            .Produces<TrialBalanceResult>().RequireAuthorization().WithTags("RPT01 Trial balance");
        app.MapGet(p + "/reports/accounts/{accountId:guid}/ledger", async (Guid companyId, Guid accountId,
            [FromQuery] string from, [FromQuery] string to, Guid? branchId, int? offset, int? limit, Guid? costCenterId, string? externalReference,
            ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.AccountLedger(companyId, accountId, Actor(h), Date(from), Date(to), branchId, offset ?? 0, limit ?? 50, ct, costCenterId, externalReference)))
            .Produces<AccountLedgerResult>().RequireAuthorization().WithTags("RPT02 Account ledger");
        app.MapGet(p + "/audit-events", async (Guid companyId, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAudit(companyId, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS14 Audit");
        app.MapGet(p + "/recurring-journals", async (Guid companyId, Guid? branchId, bool? isActive, int? offset, int? limit, string? search, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListRecurringJournals(companyId, Actor(h), branchId, isActive, offset ?? 0, limit ?? 50, ct, search))).Produces<Page<RecurringJournalSummary>>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetRecurringJournal(companyId, id, Actor(h), ct))).Produces<RecurringJournalTemplate>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPost(p + "/recurring-journals", async (Guid companyId, CreateRecurringJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateRecurringJournal(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPut(p + "/recurring-journals/{id:guid}", async (Guid companyId, Guid id, EditRecurringJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditRecurringJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPost(p + "/recurring-journals/{id:guid}/status", async (Guid companyId, Guid id, SetRecurringStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetRecurringStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPost(p + "/recurring-journals/{id:guid}/generate", async (Guid companyId, Guid id, GenerateRecurringJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GenerateRecurringJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}/generations", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListRecurringGenerations(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<RecurringJournalGeneration>>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}/schedule", async (Guid companyId, Guid id, Guid fiscalYearId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.RecurringSchedule(companyId, id, fiscalYearId, Actor(h), ct))).Produces<IReadOnlyList<RecurringScheduleRow>>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListRecurringChanges(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("GL04 Recurring journals");
    }
}
