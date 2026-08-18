using System.Net;
using System.Text.Json;
using Appizza.Modules.Establishments;
using Appizza.Modules.Devices;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Appizza.Worker;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryContestationApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task AutomaticDeliveryCanBeContestedWithinWindow()
    {
        var s = await SeedAutomatic();
        var key = Guid.NewGuid();
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", customerNote = "Ainda não chegou", expectedVersion = s.Version }, s.DeviceToken, key);
        response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var c = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); var contest = await db.Set<DeliveryContest>().SingleAsync(x => x.ProductionItemId == s.ProductionItemId);
        Assert.Equal("contested", c.Status); Assert.Equal("automatic", c.ConfirmationSource); Assert.Equal("open", contest.Status); Assert.Equal(s.ProductionItemId, contest.ProductionItemId); Assert.Equal(s.EstablishmentId, contest.EstablishmentId);
        Assert.Equal("awaiting_delivery_confirmation", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contested.v1"));
    }

    [Fact]
    public async Task ContestReplayIsIdempotentAndDifferentPayloadIsRejected()
    {
        var s = await SeedAutomatic(); var key = Guid.NewGuid(); var body = new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version };
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", body, s.DeviceToken, key); first.EnsureSuccessStatusCode();
        var replay = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", body, s.DeviceToken, key); replay.EnsureSuccessStatusCode();
        var different = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "OTHER", expectedVersion = s.Version }, s.DeviceToken, key);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode); Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(different));
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryContest>().CountAsync(x => x.ProductionItemId == s.ProductionItemId)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contested.v1"));
    }

    [Fact]
    public async Task ContestationWindowExpiredIsRejectedWithoutMutation()
    {
        var s = await SeedAutomatic(); await using (var db = fixture.CreateDbContext()) { var c = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); c.ConfirmedAt = DateTimeOffset.UtcNow.AddMinutes(-10); await db.SaveChangesAsync(); }
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("DELIVERY_CONTESTATION_WINDOW_EXPIRED", await fixture.ErrorCodeAsync(response));
        await using var after = fixture.CreateDbContext(); Assert.Equal("confirmed_automatic", await after.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await after.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Fact]
    public async Task ManualConfirmationCannotBeContested()
    {
        var s = await SeedAutomatic(); var device = s.DeviceToken; await using (var prep = fixture.CreateDbContext()) { var c = await prep.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); var p = await prep.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); c.Status = "pending"; c.Version++; p.Status = "awaiting_delivery_confirmation"; await prep.SaveChangesAsync(); } await using var currentDb = fixture.CreateDbContext(); var currentVersion = await currentDb.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync(); var confirm = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = currentVersion }, device, Guid.NewGuid()); confirm.EnsureSuccessStatusCode();
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version + 1 }, device, Guid.NewGuid());
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_manual", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Fact]
    public async Task StaleVersionAndCrossTenantCannotContest()
    {
        var s = await SeedAutomatic();
        var stale = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version - 1 }, s.DeviceToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("CONCURRENCY_CONFLICT", await fixture.ErrorCodeAsync(stale));
        var other = await fixture.CreateTenantAsync(2, 1); var otherDevice = await fixture.RegisterAndBindAsync(other.AccessToken, other.TableIds[0]); await fixture.OpenSessionAsync(otherDevice.AccessToken);
        var foreign = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, otherDevice.AccessToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_automatic", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Fact]
    public async Task BlockedDeviceCannotContest()
    {
        var s = await SeedAutomatic(); await fixture.PostAsync($"api/v1/operations/table-devices/{s.DeviceId}/block", null, s.TenantToken); var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal("DEVICE_BLOCKED", await fixture.ErrorCodeAsync(response));
        await using var db = fixture.CreateDbContext(); Assert.Empty(await db.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Fact]
    public async Task AlternativeSessionCannotContestOriginalOrder()
    {
        var s = await SeedAutomatic(); var tenant = await fixture.CreateTenantAsync(2, 1); var otherDevice = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); await fixture.OpenSessionAsync(otherDevice.AccessToken);
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, otherDevice.AccessToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_automatic", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Fact]
    public async Task RevokedCredentialCannotContest()
    {
        var s = await SeedAutomatic(); await using (var db = fixture.CreateDbContext()) { var device = await db.Set<Device>().SingleAsync(x => x.Id == s.DeviceId); device.CredentialVersion++; await db.SaveChangesAsync(); }
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal("DEVICE_CREDENTIAL_REVOKED", await fixture.ErrorCodeAsync(response));
        await using var after = fixture.CreateDbContext(); Assert.Empty(await after.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Theory]
    [InlineData("pending", "delivered")]
    [InlineData("confirmed_manual", "delivered")]
    [InlineData("contested", "awaiting_delivery_confirmation")]
    [InlineData("superseded", "delivered")]
    public async Task NonAutomaticConfirmationStatesAreRejected(string confirmationStatus, string productionStatus)
    {
        var s = await SeedAutomatic(); await using (var db = fixture.CreateDbContext()) { var c = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); var p = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); c.Status = confirmationStatus; p.Status = productionStatus; await db.SaveChangesAsync(); }
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        await using var after = fixture.CreateDbContext(); Assert.Equal(confirmationStatus, await after.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await after.Set<DeliveryContest>().Where(x => x.ProductionItemId == s.ProductionItemId).ToListAsync());
    }

    [Fact]
    public async Task HistoryAndFinancialValuesRemainUnchangedExceptLifecycle()
    {
        var s = await SeedAutomatic(); await using var before = fixture.CreateDbContext(); var c = await before.Set<DeliveryConfirmation>().AsNoTracking().SingleAsync(x => x.Id == s.ConfirmationId); var item = await before.Set<OrderItem>().AsNoTracking().SingleAsync(x => x.Id == s.OrderItemId); var order = await before.Set<Order>().AsNoTracking().SingleAsync(x => x.Id == s.OrderId); var revisions = await before.Set<OrderItemRevision>().AsNoTracking().Where(x => x.OrderItemId == s.OrderItemId).Select(x => x.Id).ToListAsync();
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); response.EnsureSuccessStatusCode();
        await using var after = fixture.CreateDbContext(); var c2 = await after.Set<DeliveryConfirmation>().AsNoTracking().SingleAsync(x => x.Id == s.ConfirmationId); var item2 = await after.Set<OrderItem>().AsNoTracking().SingleAsync(x => x.Id == s.OrderItemId); var order2 = await after.Set<Order>().AsNoTracking().SingleAsync(x => x.Id == s.OrderId); Assert.Equal(c.Id, c2.Id); Assert.Equal(c.SequenceNumber, c2.SequenceNumber); Assert.Equal(c.RequestedAt, c2.RequestedAt); Assert.Equal(c.ExpiresAt, c2.ExpiresAt); Assert.Equal(c.ConfirmedAt, c2.ConfirmedAt); Assert.Equal("automatic", c2.ConfirmationSource); Assert.Equal(item.UnitAmount, item2.UnitAmount); Assert.Equal(item.TotalAmount, item2.TotalAmount); Assert.Equal(item.CurrentRevisionNumber, item2.CurrentRevisionNumber); Assert.Equal(order.TotalAmount, order2.TotalAmount); Assert.Equal(revisions, await after.Set<OrderItemRevision>().Where(x => x.OrderItemId == s.OrderItemId).Select(x => x.Id).ToListAsync()); var contest = await after.Set<DeliveryContest>().SingleAsync(x => x.ProductionItemId == s.ProductionItemId); Assert.Equal("open", contest.Status); Assert.Null(contest.ResolvedAt); Assert.Null(contest.Resolution);
    }

    [Fact]
    public async Task ExistingOpenContestCannotBeOpenedAgain()
    {
        var s = await SeedAutomatic(); var first = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); first.EnsureSuccessStatusCode(); var second = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version + 1 }, s.DeviceToken, Guid.NewGuid()); Assert.Contains(second.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict }); await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryContest>().CountAsync(x => x.ProductionItemId == s.ProductionItemId)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contested.v1"));
    }

    [Fact]
    public async Task ContestationLeavesTableSessionFinancialsUnchanged()
    {
        var s = await SeedAutomatic(); await using var before = fixture.CreateDbContext(); var sessionId = await before.Set<Order>().Where(x => x.Id == s.OrderId).Select(x => x.TableSessionId).SingleAsync(); var session = await before.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == sessionId); var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); response.EnsureSuccessStatusCode(); await using var after = fixture.CreateDbContext(); var sessionAfter = await after.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == sessionId); Assert.Equal(session.SubtotalAmount, sessionAfter.SubtotalAmount); Assert.Equal(session.TotalAmount, sessionAfter.TotalAmount); Assert.Equal(session.RemainingAmount, sessionAfter.RemainingAmount);
    }

    [Fact]
    public async Task ContestRaceProducesOneOpenContest()
    {
        var s = await SeedAutomatic(); var responses = await fixture.ConcurrentAsync(
            () => fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()),
            () => fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound); Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryContest>().CountAsync(x => x.ProductionItemId == s.ProductionItemId && x.Status == "open")); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contested.v1"));
    }

    [Fact]
    public async Task ContestRaceWinnerAndLoserReplayRemainIdempotent()
    {
        var s = await SeedAutomatic(); var keyA = Guid.NewGuid(); var keyB = Guid.NewGuid(); var body = new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version };
        var taskA = fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", body, s.DeviceToken, keyA);
        var taskB = fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", body, s.DeviceToken, keyB);
        await Task.WhenAll(taskA, taskB); var responseA = await taskA; var responseB = await taskB;
        var winnerKey = responseA.StatusCode == HttpStatusCode.OK ? keyA : keyB; var loserKey = responseA.StatusCode == HttpStatusCode.OK ? keyB : keyA; var winnerResponse = responseA.StatusCode == HttpStatusCode.OK ? responseA : responseB; var loserResponse = responseA.StatusCode == HttpStatusCode.OK ? responseB : responseA; Assert.Equal(HttpStatusCode.OK, winnerResponse.StatusCode); Assert.Contains(loserResponse.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        await using var baselineDb = fixture.CreateDbContext(); var baselineContest = await baselineDb.Set<DeliveryContest>().SingleAsync(x => x.ProductionItemId == s.ProductionItemId); var baselinePiVersion = await baselineDb.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync(); var baselineConfirmationVersion = await baselineDb.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Version).SingleAsync(); var baselineOutbox = await baselineDb.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contested.v1");
        var winnerReplay = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", body, s.DeviceToken, winnerKey); winnerReplay.EnsureSuccessStatusCode();
        var loserReplay = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", body, s.DeviceToken, loserKey); Assert.Contains(loserReplay.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        await using var afterDb = fixture.CreateDbContext(); var afterContest = await afterDb.Set<DeliveryContest>().SingleAsync(x => x.ProductionItemId == s.ProductionItemId); Assert.Equal(baselineContest.Id, afterContest.Id); Assert.Equal(baselinePiVersion, await afterDb.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync()); Assert.Equal(baselineConfirmationVersion, await afterDb.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Version).SingleAsync()); Assert.Equal(baselineOutbox, await afterDb.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contested.v1")); Assert.Equal(1, await afterDb.Set<DeliveryContest>().CountAsync(x => x.ProductionItemId == s.ProductionItemId));
    }

    [Fact]
    public async Task OpenContestAppearsAsAttentionRequired()
    {
        var s = await SeedAutomatic(); var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = s.Version }, s.DeviceToken, Guid.NewGuid()); response.EnsureSuccessStatusCode();
        var status = await fixture.GetAsync("api/v1/table-device/session/orders/status", s.DeviceToken); status.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync()); var order = json.RootElement.GetProperty("orders").EnumerateArray().Single(x => x.GetProperty("orderId").GetGuid() == s.OrderId); Assert.Equal("attention_required", order.GetProperty("items")[0].GetProperty("publicSubstatus").GetString()); Assert.Contains("delivery_contest_open", order.GetProperty("items")[0].GetProperty("attentionReasons").EnumerateArray().Select(x => x.GetString()));
    }

    private async Task<Seeded> SeedAutomatic()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var sessionId = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; var orderId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var productionId = Guid.NewGuid(); var stationId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext()) { db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = $"F4-{Guid.NewGuid():N}", IsDefault = true, CreatedAt = now, UpdatedAt = now }); db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = sessionId, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }); db.Add(new OrderItem { Id = itemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "F4", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now }); db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = itemId, StationId = stationId, Status = "ready", ReadyAt = now, AcceptedAt = now, ReceivedAt = now, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); }
        await using var db2 = fixture.CreateDbContext(); var version = await db2.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync(); var send = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{productionId}/send-to-table", new { expectedVersion = version }, tenant.AccessToken, Guid.NewGuid()); send.EnsureSuccessStatusCode(); await using var db3 = fixture.CreateDbContext(); var confirmation = await db3.Set<DeliveryConfirmation>().SingleAsync(x => x.ProductionItemId == productionId); confirmation.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db3.SaveChangesAsync(); var worker = await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync(); Assert.Equal(1, worker); await using var db4 = fixture.CreateDbContext(); var current = await db4.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync(); return new(tenant.EstablishmentId, tenant.AccessToken, device.DeviceId, orderId, itemId, productionId, confirmation.Id, current, device.AccessToken);
    }
    private sealed record Seeded(Guid EstablishmentId, string TenantToken, Guid DeviceId, Guid OrderId, Guid OrderItemId, Guid ProductionItemId, Guid ConfirmationId, long Version, string DeviceToken);
}
