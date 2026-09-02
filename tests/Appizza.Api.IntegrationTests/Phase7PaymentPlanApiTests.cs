using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
#pragma warning disable CA1861

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7PaymentPlanApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task TotalCreatesAuthoritativePlanAndAllocation()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await Create(context, new { mode = "total" });
        await AssertCreated(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("total", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt64());
        Assert.Single(json.RootElement.GetProperty("allocations").EnumerateArray());
        await using var db = fixture.CreateDbContext();
        var planId = json.RootElement.GetProperty("planId").GetGuid();
        var plan = await db.Set<PaymentPlan>().Include(x => x.Allocations).SingleAsync(x => x.Id == planId);
        Assert.NotEqual(Guid.Empty, plan.LogicalPlanId);
        Assert.Equal(1, plan.Version);
        Assert.Null(plan.Allocations.Single().ParticipantId);
    }

    [Fact]
    public async Task TotalUsesPaidAndReservedBalanceWithoutMutatingSession()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.TotalAmount = 100m; session.PaidAmount = 20m; session.ReservedAmount = 10m;
            await db.SaveChangesAsync();
        }
        var response = await Create(context, new { mode = "total" });
        await AssertCreated(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(70m, json.RootElement.GetProperty("allocations")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(80m, json.RootElement.GetProperty("authoritative").GetProperty("outstandingAmount").GetDecimal());
        Assert.Equal(70m, json.RootElement.GetProperty("authoritative").GetProperty("availableToReserveAmount").GetDecimal());
    }

    [Fact]
    public async Task EqualSplitUsesDeterministicResidual()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await SetTotal(context.SessionId, 100m);
        var response = await Create(context, new { mode = "equal_split", partCount = 3 });
        await AssertCreated(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, json.RootElement.GetProperty("allocations").EnumerateArray().Select(x => x.GetProperty("amount").GetDecimal()).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EqualSplitRejectsInvalidPartCount(int count)
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await Create(context, new { mode = "equal_split", partCount = count });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CustomAmountRejectsExcessPrecisionAndAcceptsValidAmount()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await SetTotal(context.SessionId, 100m);
        await AssertCreated(await Create(context, new { mode = "custom_amount", amount = 25.00m }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(context, new { mode = "custom_amount", amount = 25.001m })).StatusCode);
    }

    [Theory]
    [InlineData("by_participant")]
    [InlineData("participants")]
    [InlineData("items")]
    [InlineData("amount")]
    public async Task LegacyModesAreRejected(string mode)
    {
        var context = await fixture.CreateOpenSessionAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(context, new { mode })).StatusCode);
    }

    [Fact]
    public async Task SameIdempotencyKeyReplaysSinglePlan()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, key);
        var replay = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var firstJson = JsonNode.Parse(await first.Content.ReadAsStringAsync())!.AsObject();
        var replayJson = JsonNode.Parse(await replay.Content.ReadAsStringAsync())!.AsObject();
        foreach (var property in new[] { "planId", "version", "mode", "tableSessionId", "allocations", "authoritative" })
        {
            Assert.True(JsonNode.DeepEquals(firstJson[property], replayJson[property]),
                $"Replay property '{property}' differs.");
        }
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<PaymentPlan>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Fact]
    public async Task TotalWithZeroAvailableBalanceCreatesZeroAllocation()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.TotalAmount = 10m; session.PaidAmount = 10m; session.ReservedAmount = 0m;
            await db.SaveChangesAsync();
        }
        var response = await Create(context, new { mode = "total" });
        await AssertCreated(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0m, json.RootElement.GetProperty("allocations")[0].GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task CustomAmountInvalidValuesAreRejected(decimal amount)
    {
        var context = await fixture.CreateOpenSessionAsync();
        await SetTotal(context.SessionId, 100m);
        var response = await Create(context, new { mode = "custom_amount", amount });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.Set<PaymentPlan>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Fact]
    public async Task SameKeyWithDifferentIntentConflicts()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await SetTotal(context.SessionId, 100m);
        var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, key);
        var second = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "custom_amount", amount = 10m }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SameKeyConcurrentRequestsCreateSinglePlan()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await SetTotal(context.SessionId, 100m);
        var key = Guid.NewGuid();
        var requests = Enumerable.Range(0, 2).Select(_ => fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, key));
        var responses = await Task.WhenAll(requests);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<PaymentPlan>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Fact]
    public async Task ByItemRejectsEmptyAndDuplicateItemsWithoutPersistence()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var empty = await Create(context, new { mode = "by_item", items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var id = Guid.NewGuid();
        var duplicate = await Create(context, new { mode = "by_item", items = new[] { new { orderItemId = id }, new { orderItemId = id } } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.Set<PaymentPlan>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Fact]
    public async Task ByItemRejectsUnknownItemWithoutDisclosure()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await Create(context, new { mode = "by_item", items = new[] { new { orderItemId = Guid.NewGuid() } } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ByItemCreatesAuthoritativeAllocation()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await SetTotal(context.SessionId, 100m);
        var item = await AddOrderItem(context.SessionId, context.Device.DeviceId, 42m, 2);
        var response = await Create(context, new { mode = "by_item", items = new[] { new { orderItemId = item.Id } } });
        await AssertCreated(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("by_item", json.RootElement.GetProperty("mode").GetString());
        var allocation = json.RootElement.GetProperty("allocations")[0];
        Assert.Equal(item.Id, allocation.GetProperty("orderItemId").GetGuid());
        Assert.Equal(item.TotalAmount, allocation.GetProperty("amount").GetDecimal());
        Assert.Equal(0, allocation.GetProperty("stableOrder").GetInt32());
        Assert.True(allocation.GetProperty("participantId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task ByItemCreatesMultipleAuthoritativeAllocationsAndCanonicalOrder()
    {
        var context = await fixture.CreateOpenSessionAsync(); await SetTotal(context.SessionId, 100m);
        var a = await AddOrderItem(context.SessionId, context.Device.DeviceId, 10m, 1);
        var b = await AddOrderItem(context.SessionId, context.Device.DeviceId, 20m, 1);
        var response = await Create(context, new { mode = "by_item", items = new[] { new { orderItemId = b.Id }, new { orderItemId = a.Id } } });
        await AssertCreated(response);
        await using var db = fixture.CreateDbContext();
        var planId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("planId").GetGuid();
        var allocations = await db.Set<PaymentPlanAllocation>().Where(x => x.PaymentPlanId == planId).OrderBy(x => x.StableOrder).ToListAsync();
        Assert.Equal(2, allocations.Count); Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x).ToArray(), allocations.Select(x => x.OrderItemId!.Value).OrderBy(x => x).ToArray());
        var canonical = new[] { a.Id, b.Id }.OrderBy(x => x).ToArray();
        Assert.Equal(canonical, allocations.Select(x => x.OrderItemId!.Value).ToArray());
    }

    [Fact]
    public async Task ByItemReversedItemsReplaySamePlan()
    {
        var context = await fixture.CreateOpenSessionAsync(); await SetTotal(context.SessionId, 100m);
        var a = await AddOrderItem(context.SessionId, context.Device.DeviceId, 10m, 1); var b = await AddOrderItem(context.SessionId, context.Device.DeviceId, 20m, 1); var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "by_item", items = new[] { new { orderItemId = a.Id }, new { orderItemId = b.Id } } }, context.Device.AccessToken, key);
        var replay = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "by_item", items = new[] { new { orderItemId = b.Id }, new { orderItemId = a.Id } } }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var x = JsonNode.Parse(await first.Content.ReadAsStringAsync()); var y = JsonNode.Parse(await replay.Content.ReadAsStringAsync()); Assert.True(JsonNode.DeepEquals(x, y));
    }

    [Fact]
    public async Task ByItemDifferentSetConflicts()
    {
        var context = await fixture.CreateOpenSessionAsync(); await SetTotal(context.SessionId, 100m);
        var a = await AddOrderItem(context.SessionId, context.Device.DeviceId, 10m, 1); var b = await AddOrderItem(context.SessionId, context.Device.DeviceId, 20m, 1); var c = await AddOrderItem(context.SessionId, context.Device.DeviceId, 30m, 1); var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "by_item", items = new[] { new { orderItemId = a.Id }, new { orderItemId = b.Id } } }, context.Device.AccessToken, key);
        var second = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "by_item", items = new[] { new { orderItemId = a.Id }, new { orderItemId = c.Id } } }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ByItemRejectsCancelledItem()
    {
        var context = await fixture.CreateOpenSessionAsync(); var item = await AddOrderItem(context.SessionId, context.Device.DeviceId, 10m, 1);
        await using (var db = fixture.CreateDbContext()) { var entity = await db.Set<OrderItem>().SingleAsync(x => x.Id == item.Id); entity.CommercialStatus = "cancelled"; await db.SaveChangesAsync(); }
        var response = await Create(context, new { mode = "by_item", items = new[] { new { orderItemId = item.Id } } }); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ByItemRejectsItemsAboveAvailableBalanceWithoutPersistence()
    {
        var context = await fixture.CreateOpenSessionAsync(); await SetTotal(context.SessionId, 10m); var item = await AddOrderItem(context.SessionId, context.Device.DeviceId, 20m, 1);
        var response = await Create(context, new { mode = "by_item", items = new[] { new { orderItemId = item.Id } } }); Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ByItemRejectsItemFromDifferentSession()
    {
        var contextA = await fixture.CreateOpenSessionAsync();
        var sessionB = await CreateSecondSession(contextA.SessionId);
        var item = await AddOrderItem(sessionB, contextA.Device.DeviceId, 10m, 1);
        var response = await Create(contextA, new { mode = "by_item", items = new[] { new { orderItemId = item.Id } } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ByItemRejectsCrossTenantItemWithoutDisclosure()
    {
        var contextA = await fixture.CreateOpenSessionAsync(); var contextB = await fixture.CreateOpenSessionAsync();
        var item = await AddOrderItem(contextB.SessionId, contextB.Device.DeviceId, 10m, 1);
        var response = await Create(contextA, new { mode = "by_item", items = new[] { new { orderItemId = item.Id } } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(item.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> CreateSecondSession(Guid sourceSessionId)
    {
        await using var db = fixture.CreateDbContext();
        var source = await db.Set<TableSession>().SingleAsync(x => x.Id == sourceSessionId);
        var now = DateTimeOffset.UtcNow; var tableId = Guid.NewGuid();
        db.Add(new DiningTable { Id = tableId, EstablishmentId = source.EstablishmentId, Name = "PaymentPlan test table", Status = "available", DisplayOrder = 99, CreatedAt = now, UpdatedAt = now, Version = 1 });
        var session = new TableSession { Id = Guid.NewGuid(), EstablishmentId = source.EstablishmentId, DiningTableId = tableId, SessionNumber = Guid.NewGuid().ToString("N"), Status = "open", CustomerIdentificationStatus = "pending", OpenedAt = now, CreatedAt = now, UpdatedAt = now, Version = 1 };
        db.Add(session); await db.SaveChangesAsync(); return session.Id;
    }

    private async Task<OrderItem> AddOrderItem(Guid sessionId, Guid sourceDeviceId, decimal total, int quantity)
    {
        await using var db = fixture.CreateDbContext();
        var session = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId);
        var now = DateTimeOffset.UtcNow;
        var order = new Order { Id = Guid.NewGuid(), EstablishmentId = session.EstablishmentId, TableSessionId = sessionId, SourceDeviceId = sourceDeviceId, ClientSubmissionId = Guid.NewGuid(), Status = "submitted", SubtotalAmount = total, TotalAmount = total, SubmittedAt = now, CreatedAt = now, UpdatedAt = now, Version = 1 };
        var item = new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "product", ProductName = "Test item", Quantity = quantity, UnitAmount = total / quantity, TotalAmount = total, ConfigurationVersion = "test", CommercialStatus = "submitted", CatalogRevisionId = Guid.NewGuid(), Snapshot = "{}", CreatedAt = now, UpdatedAt = now, Version = 1, CurrentRevisionNumber = 1 };
        db.Add(order); db.Add(item); await db.SaveChangesAsync(); return item;
    }

    private async Task<HttpResponseMessage> Create(Phase1ApiFixture.OpenSessionContext context, object body)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["sessionId"] = context.SessionId;
        return await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", json, context.Device.AccessToken, Guid.NewGuid());
    }
    private static async Task AssertCreated(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }
    private async Task SetTotal(Guid sessionId, decimal total) { await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId); s.TotalAmount = total; s.RemainingAmount = total; await db.SaveChangesAsync(); }
}
