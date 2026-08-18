using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryHistoryInvariantTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ConfirmDeliveredPreservesConfirmationAndContestHistory()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var token = await ResolveToken(s.EstablishmentId);
        await using var beforeDb = fixture.CreateDbContext();
        var before = await beforeDb.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.DeliveryConfirmationId);
        var contestBefore = await beforeDb.Set<DeliveryContest>().SingleAsync(x => x.Id == s.DeliveryContestId);
        var financialBefore = await Financial(beforeDb, s.OrderItemId);
        (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.DeliveryContestId}/resolve", new { resolution = "confirm_delivered", expectedVersion = s.ProductionItemVersion }, token, Guid.NewGuid())).EnsureSuccessStatusCode();
        await using var afterDb = fixture.CreateDbContext();
        var after = await afterDb.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.DeliveryConfirmationId);
        var contestAfter = await afterDb.Set<DeliveryContest>().SingleAsync(x => x.Id == s.DeliveryContestId);
        Assert.Equal(financialBefore, await Financial(afterDb, s.OrderItemId));
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.SequenceNumber, after.SequenceNumber); Assert.Equal(before.RequestedAt, after.RequestedAt); Assert.Equal(before.ExpiresAt, after.ExpiresAt); Assert.Equal(before.ConfirmedAt, after.ConfirmedAt); Assert.Equal(before.ConfirmationSource, after.ConfirmationSource);
        Assert.Equal(contestBefore.Id, contestAfter.Id); Assert.Equal("resolved_delivered", contestAfter.Status); Assert.NotNull(contestAfter.ResolvedAt); Assert.Equal(1, await afterDb.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId)); Assert.Equal("delivered", await afterDb.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
        var status = await fixture.GetAsync("api/v1/table-device/session/orders/status", s.DeviceToken); status.EnsureSuccessStatusCode(); Assert.Contains("delivered", await status.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryThenSendPreservesSequenceOneAndAppendsSequenceTwo()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var token = await ResolveToken(s.EstablishmentId);
        await using var beforeDb = fixture.CreateDbContext();
        var before = await beforeDb.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.DeliveryConfirmationId);
        var attemptsBefore = await beforeDb.Set<ProductionAttempt>().Where(x => x.ProductionItemId == s.ProductionItemId).Select(x => new { x.Id, x.AttemptNumber, x.Status }).ToListAsync();
        var financialBefore = await Financial(beforeDb, s.OrderItemId);
        (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.DeliveryContestId}/resolve", new { resolution = "retry_delivery", expectedVersion = s.ProductionItemVersion }, token, Guid.NewGuid())).EnsureSuccessStatusCode();
        await using var readyDb = fixture.CreateDbContext();
        var readyVersion = await readyDb.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync();
        using (var beforeJson = JsonDocument.Parse(await (await fixture.GetAsync("api/v1/table-device/session/orders/status", s.DeviceToken)).Content.ReadAsStringAsync()))
        {
            var order = beforeJson.RootElement.GetProperty("orders").EnumerateArray().Single(x => x.GetProperty("orderId").GetGuid() == s.OrderId);
            Assert.False(order.TryGetProperty("attentionRequired", out var attention) && attention.GetBoolean());
            Assert.NotEqual("delivered", order.GetProperty("items")[0].GetProperty("publicStatus").GetString());
        }
        (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{s.ProductionItemId}/send-to-table", new { expectedVersion = readyVersion }, s.EstablishmentToken, Guid.NewGuid())).EnsureSuccessStatusCode();
        await using var afterDb = fixture.CreateDbContext();
        var confirmations = await afterDb.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == s.ProductionItemId).OrderBy(x => x.SequenceNumber).ToListAsync();
        Assert.Equal(2, confirmations.Count); Assert.Equal(before.Id, confirmations[0].Id); Assert.Equal(1, confirmations[0].SequenceNumber); Assert.Equal("superseded", confirmations[0].Status); Assert.Equal(before.RequestedAt, confirmations[0].RequestedAt); Assert.Equal(before.ExpiresAt, confirmations[0].ExpiresAt); Assert.Equal(before.ConfirmedAt, confirmations[0].ConfirmedAt); Assert.NotEqual(before.Id, confirmations[1].Id); Assert.Equal(2, confirmations[1].SequenceNumber); Assert.Equal("pending", confirmations[1].Status);
        var attemptsAfter = await afterDb.Set<ProductionAttempt>().Where(x => x.ProductionItemId == s.ProductionItemId).Select(x => new { x.Id, x.AttemptNumber, x.Status }).ToListAsync(); Assert.Equal(attemptsBefore, attemptsAfter);
        Assert.Equal(financialBefore, await Financial(afterDb, s.OrderItemId));
        Assert.Equal(1, await afterDb.Set<DeliveryContest>().CountAsync(x => x.Id == s.DeliveryContestId && x.Status == "resolved_retry"));
        using var afterJson = JsonDocument.Parse(await (await fixture.GetAsync("api/v1/table-device/session/orders/status", s.DeviceToken)).Content.ReadAsStringAsync());
        var afterOrder = afterJson.RootElement.GetProperty("orders").EnumerateArray().Single(x => x.GetProperty("orderId").GetGuid() == s.OrderId);
        Assert.False(afterOrder.TryGetProperty("attentionRequired", out var afterAttention) && afterAttention.GetBoolean());
        Assert.NotEqual("delivered", afterOrder.GetProperty("items")[0].GetProperty("publicStatus").GetString());
    }

    private static async Task<(decimal Unit, decimal Total, int Revision, int RevisionCount, decimal OrderSubtotal, decimal OrderTotal, decimal SessionSubtotal, decimal SessionTotal)> Financial(Appizza.Persistence.AppizzaDbContext db, Guid orderItemId)
    {
        var item = await db.Set<OrderItem>().SingleAsync(x => x.Id == orderItemId);
        var order = await db.Set<Order>().SingleAsync(x => x.Id == item.OrderId);
        var session = await db.Set<TableSession>().SingleAsync(x => x.Id == order.TableSessionId);
        return (item.UnitAmount, item.TotalAmount, item.CurrentRevisionNumber, await db.Set<OrderItemRevision>().CountAsync(x => x.OrderItemId == orderItemId), order.SubtotalAmount, order.TotalAmount, session.SubtotalAmount, session.TotalAmount);
    }

    private async Task<string> ResolveToken(Guid establishmentId)
    {
        await using var db = fixture.CreateDbContext();
        var permission = await db.Set<Permission>().SingleOrDefaultAsync(x => x.Code == "kitchen.delivery.resolve");
        if (permission is null) { permission = new Permission { Id = Guid.NewGuid(), Code = "kitchen.delivery.resolve", Module = "kitchen", Name = "kitchen.delivery.resolve" }; db.Add(permission); await db.SaveChangesAsync(); }
        return await fixture.CreateUserTokenAsync(establishmentId, "kitchen.delivery.resolve");
    }
}
