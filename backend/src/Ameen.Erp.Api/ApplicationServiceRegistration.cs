using Ameen.Erp.Application;

namespace Ameen.Erp.Api;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddErpApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<AuditService>();
        services.AddScoped<BranchService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<CostCenterService>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<ExchangeRateService>();
        services.AddScoped<FiscalCalendarService>();
        services.AddScoped<ItemGroupService>();
        services.AddScoped<InventoryIdentityService>();
        services.AddScoped<InventoryOpeningService>();
        services.AddScoped<ItemService>();
        services.AddScoped<JournalService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<PeriodCloseService>();
        services.AddScoped<RecurringJournalService>();
        services.AddScoped<ReportService>();
        services.AddScoped<SetupQueryService>();
        services.AddScoped<UnitService>();
        services.AddScoped<WarehouseService>();
        return services;
    }
}
