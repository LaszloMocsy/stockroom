using Microsoft.Extensions.Options;
using Stockroom.Api.Configuration;
using Stockroom.Data.Stock;

namespace Stockroom.Api.Jobs;

/// <summary>
/// Checks stock levels against the ledger at startup and then every
/// <see cref="StockroomOptions.ReconciliationInterval"/>, and logs a warning for each drifted level
/// (spec 3.2, rule 6). It only reports: repairing drift is a decision for a person.
/// </summary>
internal sealed partial class StockReconciliationJob(
    IServiceScopeFactory scopes,
    IOptions<StockroomOptions> options,
    TimeProvider time,
    ILogger<StockReconciliationJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.ReconciliationInterval;
        if (interval == TimeSpan.Zero)
        {
            Disabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(interval, time);
        do
        {
            await RunAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<StockDrift> drift;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            drift = await scope.ServiceProvider.GetRequiredService<StockReconciler>().FindDriftAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // A failed run, e.g. while the database is unreachable, must not end the job: the next tick retries.
            RunFailed(logger, ex);
            return;
        }

        if (drift.Count == 0)
        {
            NoDrift(logger);
            return;
        }

        foreach (var level in drift)
        {
            DriftFound(logger, level.ProductPublicId, level.LocationId, level.CachedQuantity, level.LedgerQuantity, level.Difference);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Stock reconciliation found no drift: every stock level matches the ledger")]
    private static partial void NoDrift(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Stock level drift for product {ProductPublicId} at location {LocationId}: the level is {CachedQuantity} but the ledger sums to {LedgerQuantity} (difference {Difference})")]
    private static partial void DriftFound(ILogger logger, Guid productPublicId, Guid locationId, int cachedQuantity, long ledgerQuantity, long difference);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Stock reconciliation failed; it runs again at the next interval")]
    private static partial void RunFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Stock reconciliation is off: STOCKROOM_RECONCILIATION_INTERVAL_SECONDS is 0")]
    private static partial void Disabled(ILogger logger);
}
