using System.Text.Json;
using Appizza.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public sealed class CatalogSignalRDispatcher(IServiceScopeFactory scopeFactory, IHubContext<Phase1Hub> hub) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly string[] SupportedEvents = ["catalog-published.v1", "ingredient-availability-changed.v1", "product-availability-changed.v1", "product-variant-availability-changed.v1"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do { await DispatchAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>();
        var messages = await db.OutboxMessages.Where(x => x.ProcessedAt == null && SupportedEvents.Contains(x.EventType)).OrderBy(x => x.OccurredAt).Take(50).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            if (message.EstablishmentId is not Guid tenant) continue;
            using var payload = JsonDocument.Parse(message.Payload); var data = payload.RootElement.GetProperty("data"); object notification;
            if (message.EventType == "catalog-published.v1") notification = new { type = "CatalogPublished", catalogVersion = ReadInt64(data, "catalogVersion") };
            else notification = new { type = "CatalogAvailabilityChanged", availabilityVersion = ReadInt64(data, "availabilityVersion") };
            await hub.Clients.Group($"establishment:{tenant}").SendAsync("CatalogInvalidated", notification, cancellationToken);
            message.ProcessedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static long ReadInt64(JsonElement data, string name) => data.TryGetProperty(name, out var value) ? value.GetInt64() : 0;
}
