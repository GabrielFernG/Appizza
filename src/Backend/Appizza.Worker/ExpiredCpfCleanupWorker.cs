using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Worker;

public sealed class ExpiredCpfCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<ExpiredCpfCleanupWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> Anonymized = LoggerMessage.Define<int>(LogLevel.Information, new EventId(2101, nameof(Anonymized)), "Anonymized {Count} expired customer identifications");
    private static readonly Action<ILogger, Exception?> CleanupFailed = LoggerMessage.Define(LogLevel.Error, new EventId(2102, nameof(CleanupFailed)), "Failed to anonymize expired customer identifications");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>();
                var now = DateTimeOffset.UtcNow;
                var expired = await db.Set<SessionCustomerIdentification>()
                    .Where(x => x.AnonymizedAt == null && x.RetentionUntil != null && x.RetentionUntil <= now)
                    .ToListAsync(stoppingToken);
                foreach (var identification in expired)
                {
                    identification.EncryptedValue = null;
                    identification.EncryptionNonce = null;
                    identification.EncryptionTag = null;
                    identification.ValueHash = null;
                    identification.AnonymizedAt = now;
                }
                if (expired.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    Anonymized(logger, expired.Count, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { CleanupFailed(logger, exception); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
