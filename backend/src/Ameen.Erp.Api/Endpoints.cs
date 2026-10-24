namespace Ameen.Erp.Api;

// Composition root only; handlers live in Endpoints/<Feature>Endpoints.cs.
public static class Endpoints
{
    public static void MapErp(this WebApplication app)
    {
        CompaniesEndpoints.Map(app);
        BranchesEndpoints.Map(app);
        WarehousesEndpoints.Map(app);
        ItemsEndpoints.Map(app);
        InventoryIdentitiesEndpoints.Map(app);
        InventoryOpeningsEndpoints.Map(app);
        ItemGroupsEndpoints.Map(app);
        UnitsEndpoints.Map(app);
        CurrenciesEndpoints.Map(app);
        ExchangeRatesEndpoints.Map(app);
        PeriodCloseEndpoints.Map(app);
        FiscalCalendarEndpoints.Map(app);
        CostCentersEndpoints.Map(app);
        MembershipsEndpoints.Map(app);
        AccountsEndpoints.Map(app);
        JournalsEndpoints.Map(app);
        GeneralOpeningsEndpoints.Map(app);
        ReportsEndpoints.Map(app);
        AuditEndpoints.Map(app);
        RecurringJournalsEndpoints.Map(app);
    }
}
