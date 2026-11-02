using Ameen.Erp.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Ameen.Erp.Api;

// Opt-in only. ActorSubject must be a real member with Reverse and Draft permissions.
// Each tenant uses an ordinary scoped service; no automatic submit, approve or post occurs.
internal sealed class ReversalDraftScheduler : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger<ReversalDraftScheduler> logger;

    public ReversalDraftScheduler(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<ReversalDraftScheduler> logger)
    {
        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subject = configuration["ReversalScheduler:ActorSubject"]!;
        var minutes = configuration.GetValue<int?>("ReversalScheduler:IntervalMinutes") ?? 15;
        if (minutes is < 1 or > 1440)
            throw new InvalidOperationException("ReversalScheduler:IntervalMinutes must be between 1 and 1440.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        do
        {
            try { await RunCycle(subject, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Scheduled reversal draft scan failed; no approval or posting is performed."); }
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
                using var scope = scopeFactory.CreateScope();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var service = scope.ServiceProvider.GetRequiredService<JournalService>();
                var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
                var result = await service.RunScheduledReversals(company.Id, new Actor(subject), asOf, 100, ct);
                if (result.SelectedCount > 0)
                    logger.LogInformation("Reversal scheduler company {CompanyId}: {Generated} drafts, {Failed} rejected journals.",
                        company.Id, result.GeneratedCount, result.FailedCount);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reversal scheduler failed for company {CompanyId}; other tenants continue independently.", company.Id);
            }
        }
    }
}
