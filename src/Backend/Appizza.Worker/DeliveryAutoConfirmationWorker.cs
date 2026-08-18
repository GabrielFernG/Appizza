using System.Text.Json;
using Appizza.Modules.Establishments;
using Appizza.Modules.Kitchen;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Appizza.Worker;

/// Processes expired delivery confirmations from persisted state. The scan is intentionally
/// exposed as a single deterministic operation so the hosted worker and integration tests use
/// exactly the same transaction and locking path.
public sealed class DeliveryAutoConfirmationWorker(IServiceScopeFactory scopeFactory)
{
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>();
        var hook = scope.ServiceProvider.GetRequiredService<IPhase5DeliveryConcurrencyHook>();
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.Set<DeliveryConfirmation>()
            .Where(x => x.Status == "pending" && x.ExpiresAt <= now)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var id in candidates)
        {
            if (await ProcessOneAsync(db, id, now, hook, cancellationToken)) processed++;
        }
        return processed;
    }

    private static async Task<bool> ProcessOneAsync(AppizzaDbContext db, Guid confirmationId, DateTimeOffset now, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct)
    {
        await hook.ReachAsync("worker-before-locks", confirmationId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var confirmationIdText = confirmationId.ToString();
        var productionId = await db.Set<DeliveryConfirmation>().Where(x => x.Id == confirmationId).Select(x => x.ProductionItemId).SingleOrDefaultAsync(ct);
        if (productionId == Guid.Empty) { await tx.RollbackAsync(ct); return false; }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE id = {productionId} FOR UPDATE", ct);
        await hook.ReachAsync("worker-after-locks", productionId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.delivery_confirmation WHERE id = {confirmationId} FOR UPDATE", ct);
        db.ChangeTracker.Clear();
        var confirmation = await db.Set<DeliveryConfirmation>().SingleOrDefaultAsync(x => x.Id == confirmationId, ct);
        if (confirmation is null) { await tx.RollbackAsync(ct); return false; }
        var item = await db.Set<ProductionItem>().SingleOrDefaultAsync(x => x.Id == confirmation.ProductionItemId && x.EstablishmentId == confirmation.EstablishmentId, ct);
        if (item is null || item.Status != "awaiting_delivery_confirmation" || confirmation.Status != "pending" || confirmation.ExpiresAt > now) { await tx.RollbackAsync(ct); return false; }
        var enabled = await db.Set<EstablishmentSetting>().Where(x => x.EstablishmentId == confirmation.EstablishmentId && x.SettingKey == Phase1SettingKeys.DeliveryAutoConfirmationEnabled).Select(x => x.SettingValue).SingleOrDefaultAsync(ct);
        if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase)) { await tx.RollbackAsync(ct); return false; }
        confirmation.Status = "confirmed_automatic";
        confirmation.ConfirmationSource = "automatic";
        confirmation.ConfirmedAt = now;
        confirmation.UpdatedAt = now;
        confirmation.Version++;
        item.Status = "delivered";
        item.UpdatedAt = now;
        var correlation = Guid.NewGuid();
        AddEvent(db, confirmation.EstablishmentId, "delivery-auto-confirmed.v1", "DeliveryAutoConfirmed", item, confirmation, now, correlation);
        AddEvent(db, confirmation.EstablishmentId, "production-item-delivered.v1", "ProductionItemDelivered", item, confirmation, now, correlation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static void AddEvent(AppizzaDbContext db, Guid establishmentId, string eventType, string name, ProductionItem item, DeliveryConfirmation confirmation, DateTimeOffset now, Guid correlation)
    {
        db.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(), EstablishmentId = establishmentId, EventType = eventType, SchemaVersion = 1,
            OccurredAt = now, CorrelationId = correlation,
            Payload = JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), eventType = name, schemaVersion = 1, occurredAtUtc = now, establishmentId, correlationId = correlation, actor = new { userId = (Guid?)null, deviceId = (Guid?)null }, data = new { productionItemId = item.Id, deliveryConfirmationId = confirmation.Id, sequence = confirmation.SequenceNumber, version = item.Version } })
        });
    }
}

public sealed class DeliveryAutoConfirmationHostedWorker(IServiceScopeFactory scopeFactory, ILogger<DeliveryAutoConfirmationHostedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                var worker = new DeliveryAutoConfirmationWorker(scopeFactory);
                await worker.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { LogScanFailed(logger, ex); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static readonly Action<ILogger, Exception?> LogScanFailed = LoggerMessage.Define(LogLevel.Error, new EventId(2201, "DeliveryAutoConfirmationFailed"), "Delivery auto-confirmation scan failed.");
}
