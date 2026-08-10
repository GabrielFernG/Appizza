using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Worker;

public sealed class OutboxMonitorWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxMonitorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly Action<ILogger, int, Exception?> LogBacklog =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1001, "OutboxBacklog"),
            "Outbox backlog observed: {OutboxBacklog}");
    private static readonly Action<ILogger, Exception?> LogObservationFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1002, "OutboxObservationFailed"),
            "Could not observe the Outbox backlog.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            await ObserveBacklogAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ObserveBacklogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>();
            var pendingCount = await dbContext.OutboxMessages
                .CountAsync(message => message.ProcessedAt == null, cancellationToken);

            LogBacklog(logger, pendingCount, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogObservationFailure(logger, exception);
        }
    }
}
