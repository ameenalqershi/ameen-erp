using Ameen.Erp.Api;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.Extensions.DependencyInjection;
namespace Ameen.Erp.Tests;

public static class ServiceCompositionTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("DI: feature services share transactional state within scope and remain isolated across scopes", async _ =>
        {
            var services = new ServiceCollection();
            services.AddScoped<MemoryStore>();
            services.AddScoped<IErpStore>(s => s.GetRequiredService<MemoryStore>());
            services.AddScoped<ITransactionRunner>(s => s.GetRequiredService<MemoryStore>());
            services.AddSingleton<IClock, FixedClock>();
            services.AddErpApplication();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var first = provider.CreateAsyncScope();
            var company = first.ServiceProvider.GetRequiredService<CompanyService>();
            var currency = first.ServiceProvider.GetRequiredService<CurrencyService>();
            var id = Guid.NewGuid(); var actor = new Actor("di-owner", true);
            await company.CreateCompany(actor, Fixture.Key(), new(id, "DI", "شركة", "YE", "SAR", 2, Fixture.Profile()), default);
            var created = await currency.CreateCurrency(id, actor, Fixture.Key(), new("USD", 2, "دولار"), default);
            Ensure((await currency.GetCurrency(id, created.ResourceId, actor, default)).Code == "USD");
            Ensure(ReferenceEquals(company, first.ServiceProvider.GetRequiredService<CompanyService>()));
            var store = first.ServiceProvider.GetRequiredService<MemoryStore>();
            var receipts = store.Count<IdempotencyRecord>(); var audits = store.Count<AuditEvent>();
            store.FailBeforeCommit = true;
            try { await currency.CreateCurrency(id, actor, Fixture.Key(), new("EUR", 2, "يورو"), default); throw new Exception("Expected rollback"); }
            catch (IOException) { }
            Ensure(store.All<Currency>().All(x => x.Code != "EUR") && store.Count<IdempotencyRecord>() == receipts && store.Count<AuditEvent>() == audits);
            await using var second = provider.CreateAsyncScope();
            Ensure(!ReferenceEquals(company, second.ServiceProvider.GetRequiredService<CompanyService>()));
            Ensure(second.ServiceProvider.GetRequiredService<MemoryStore>().Count<Company>() == 0);
            // Resolve every registered feature to catch missing dependencies and captive scopes.
            foreach (var descriptor in services.Where(x => typeof(ApplicationService).IsAssignableFrom(x.ServiceType)))
                Ensure(first.ServiceProvider.GetRequiredService(descriptor.ServiceType) is ApplicationService);
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Application service composition assertion failed"); }
}
