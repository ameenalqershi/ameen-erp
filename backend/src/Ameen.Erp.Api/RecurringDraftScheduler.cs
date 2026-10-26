using Ameen.Erp.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Ameen.Erp.Api;

// Disabled unless the deployment explicitly sets RecurringScheduler:Enabled=true and
// provisions ActorSubject as a real company member with RecurringGenerate and Draft rights.
// Uses the ordinary application authorization and transaction boundaries, never superuser access.
internal sealed class RecurringDraftScheduler : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger<RecurringDraftScheduler> logger;

    public RecurringDraftScheduler(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<RecurringDraftScheduler> logger)
    {
        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subject = configuration["RecurringScheduler:ActorSubject"]!;
        var minutes = configuration.GetValue<int?>("RecurringScheduler:IntervalMinutes") ?? 15;
        if (minutes is < 1 or > 1440)
            throw new InvalidOperationException("RecurringScheduler:IntervalMinutes must be between 1 and 1440.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        do
        {
            try { await RunCycle(subject, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Scheduled recurring draft scan failed; no automatic approval or posting is performed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycle(string subject, CancellationToken ct)
    {
        IReadOnlyList<Ameen.Erp.Domain.Company> companies;
        using (var scope = scopeFactory.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IErpStore>();
            companies = await store.CompaniesFor(subject, ct);
        }
        foreach (var company in companies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Each company is isolated in a new scoped DbContext.
                using var scope = scopeFactory.CreateScope();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var service = scope.ServiceProvider.GetRequiredService<RecurringJournalService>();
                var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
                var result = await service.RunScheduledDue(company.Id, new Actor(subject), asOf, 100, ct);
                if (result.SelectedCount > 0)
                    logger.LogInformation("Recurring scheduler company {CompanyId}: {Generated} drafts, {Failed} rejected occurrences.",
                        company.Id, result.GeneratedCount, result.FailedCount);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recurring scheduler failed for company {CompanyId}; other tenants continue independently.", company.Id);
            }
        }
    }
}
