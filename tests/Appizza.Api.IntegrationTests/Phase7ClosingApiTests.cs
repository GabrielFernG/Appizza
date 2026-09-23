using System.Net;
using System.Text.Json;
using Appizza.Modules.Auditing;
using Appizza.Modules.Ordering;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7ClosingApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task Phase7PermissionsAreSeededForFixture()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var token = await fixture.CreateUserTokenAsync(tenant.EstablishmentId, "closing.cancel");
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task OpenSessionCanStartClosingAndPersistsAuditOutboxAndBalance()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await Start(context, -1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("awaiting_payment", payload.RootElement.GetProperty("status").GetString());
        await using var db = fixture.CreateDbContext();
        var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
        Assert.Equal("awaiting_payment", session.Status);
        Assert.NotNull(session.ClosingStartedAt);
        Assert.Single(await db.OutboxMessages.Where(x => x.EstablishmentId == session.EstablishmentId && x.EventType == "session-closing-started.v1").ToListAsync());
        Assert.Single(await db.Set<AuditEntry>().Where(x => x.AggregateId == session.Id && x.Action == "closing.start").ToListAsync());
    }

    [Fact]
    public async Task AuthoritativeBalanceUsesPaidAndReservedSeparately()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.SubtotalAmount = 100m; session.TotalAmount = 100m; session.PaidAmount = 20m; session.ReservedAmount = 30m; session.RemainingAmount = 80m;
            await db.SaveChangesAsync();
        }
        var response = await fixture.GetAsync($"api/v1/table-device/session/balance?sessionId={context.SessionId}", context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(80m, json.RootElement.GetProperty("outstandingAmount").GetDecimal());
        Assert.Equal(50m, json.RootElement.GetProperty("availableToReserveAmount").GetDecimal());
    }

    [Fact]
    public async Task WrongExpectedVersionAndReplayHaveNoAdditionalMaterialEffects()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var key = Guid.NewGuid();
        var expectedVersion = await CurrentVersion(context);
        var first = await Start(context, expectedVersion, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await Start(context, expectedVersion, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var stale = await Start(context, 0, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var tenantId = await TenantId(context);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenantId && x.EventType == "session-closing-started.v1"));
        Assert.Equal(1, await db.Set<AuditEntry>().CountAsync(x => x.AggregateId == context.SessionId && x.Action == "closing.start"));
    }

    [Fact]
    public async Task NewOrdersAreRejectedAfterClosing()
    {
        var context = await fixture.CreateOpenSessionAsync();
        Assert.Equal(HttpStatusCode.OK, (await Start(context, -1)).StatusCode);
        var response = await fixture.PostAsync("api/v1/table-device/orders", new { sessionId = context.SessionId }, context.Device.AccessToken);
        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PendingOperationsRejectClosingWithoutEffects()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var tenantId = await TenantId(context); var now = DateTimeOffset.UtcNow; var station = new Station { Id = Guid.NewGuid(), EstablishmentId = tenantId, Name = "pending", IsDefault = true, CreatedAt = now, UpdatedAt = now };
            var order = new Order { Id = Guid.NewGuid(), EstablishmentId = tenantId, TableSessionId = context.SessionId, SourceDeviceId = context.Device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now };
            var item = new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "pending", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now };
            db.AddRange(station, order, item, new ProductionItem { Id = Guid.NewGuid(), EstablishmentId = tenantId, OrderItemId = item.Id, StationId = station.Id, Status = "awaiting_preparation", ReceivedAt = now, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync();
        }
        var response = await Start(context, -1);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SESSION_HAS_PENDING_OPERATIONS", await fixture.ErrorCodeAsync(response));
        await using var verify = fixture.CreateDbContext();
        var session = await verify.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
        Assert.Equal("open", session.Status); Assert.Null(session.ClosingStartedAt); Assert.Equal(1, session.Version);
        Assert.Empty(await verify.OutboxMessages.Where(x => x.EstablishmentId == session.EstablishmentId && x.EventType == "session-closing-started.v1").ToListAsync());
        Assert.Empty(await verify.Set<AuditEntry>().Where(x => x.AggregateId == session.Id && x.Action == "closing.start").ToListAsync());
    }

    [Fact]
    public async Task CancelClosingReturnsSessionToOpenAndWritesSingleEffects()
    {
        var context = await fixture.CreateOpenSessionAsync();
        Assert.Equal(HttpStatusCode.OK, (await Start(context, -1)).StatusCode);
        var token = await fixture.CreateUserTokenAsync(await TenantId(context), "closing.cancel");
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{context.SessionId}/closing/cancel", new { expectedVersion = await CurrentVersion(context), reason = "customer resumed ordering" }, token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tenantId = await TenantId(context);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("open", (await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId)).Status);
        Assert.Single(await db.OutboxMessages.Where(x => x.EventType == "session-closing-cancelled.v1" && x.EstablishmentId == tenantId).ToListAsync());
        Assert.Single(await db.Set<AuditEntry>().Where(x => x.AggregateId == context.SessionId && x.Action == "closing.cancel").ToListAsync());
    }

    [Fact]
    public async Task CrossTenantAndRevokedDeviceAreNotDisclosed()
    {
        var first = await fixture.CreateOpenSessionAsync(); var second = await fixture.CreateOpenSessionAsync();
        var foreign = await fixture.PostAsync("api/v1/table-device/session/close", new { sessionId = second.SessionId, expectedVersion = 0L }, first.Device.AccessToken, true);
        Assert.True(foreign.StatusCode == HttpStatusCode.NotFound, await Diagnostic(foreign, "Cross-tenant closing"));
        await using var db = fixture.CreateDbContext(); var device = await db.Set<Appizza.Modules.Devices.Device>().SingleAsync(x => x.Id == first.Device.DeviceId); device.Status = "revoked"; await db.SaveChangesAsync();
        var revoked = await Start(first, 0);
        Assert.True(revoked.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound, await Diagnostic(revoked, "Revoked device closing"));
    }

    [Fact]
    public async Task MissingOperationsPermissionIsForbidden()
    {
        var context = await fixture.CreateOpenSessionAsync(); Assert.Equal(HttpStatusCode.OK, (await Start(context, -1)).StatusCode);
        var token = await fixture.CreateUserTokenAsync(await TenantId(context), "closing.view");
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{context.SessionId}/closing/cancel", new { expectedVersion = 1L, reason = "no" }, token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentClosingCreatesOneTransitionAndOneOutbox()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var responses = await fixture.ConcurrentAsync(() => Start(context, -1), () => Start(context, -1));
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, x => Assert.True(x.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        var tenantId = await TenantId(context);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenantId && x.EventType == "session-closing-started.v1"));
    }

    [Fact]
    public async Task DifferentPayloadWithSameKeyReturnsConflict()
    {
        var context = await fixture.CreateOpenSessionAsync(); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await Start(context, -1, key)).StatusCode);
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/close", new { sessionId = context.SessionId, expectedVersion = 999L }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task CancelReplayDoesNotDuplicateAuditOrOutbox()
    {
        var context = await fixture.CreateOpenSessionAsync(); Assert.Equal(HttpStatusCode.OK, (await Start(context, -1)).StatusCode);
        var token = await fixture.CreateUserTokenAsync(await TenantId(context), "closing.cancel"); var key = Guid.NewGuid(); var version = await CurrentVersion(context);
        var body = new { expectedVersion = version, reason = "retry" };
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{context.SessionId}/closing/cancel", body, token, key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{context.SessionId}/closing/cancel", body, token, key)).StatusCode);
        await using var db = fixture.CreateDbContext(); var tenantId = await TenantId(context);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenantId && x.EventType == "session-closing-cancelled.v1"));
        Assert.Equal(1, await db.Set<AuditEntry>().CountAsync(x => x.AggregateId == context.SessionId && x.Action == "closing.cancel"));
    }

    [Fact]
    public async Task FinancialEffectBlocksCancelAndCleaningDoesNotReleaseClosing()
    {
        var context = await fixture.CreateOpenSessionAsync(); Assert.Equal(HttpStatusCode.OK, (await Start(context, -1)).StatusCode);
        await using (var db = fixture.CreateDbContext()) { var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); session.PaidAmount = 1m; await db.SaveChangesAsync(); }
        var token = await fixture.CreateUserTokenAsync(await TenantId(context), "closing.cancel", "tables.cleaning.confirm");
        var cancel = await fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{context.SessionId}/closing/cancel", new { expectedVersion = await CurrentVersion(context), reason = "paid" }, token, Guid.NewGuid());
        Assert.True(cancel.StatusCode == HttpStatusCode.Conflict, await Diagnostic(cancel, "Cancel with financial effect"));
        var tableId = await TableId(context); var cleaning = await fixture.PostAsync($"api/v1/operations/tables/{tableId}/confirm-cleaning", null, token);
        Assert.True(cleaning.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound, await Diagnostic(cleaning, "Cleaning before closed"));
    }

    [Fact]
    public async Task ClientCannotOverrideAuthoritativeFinancialValues()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/close", new { sessionId = context.SessionId, expectedVersion = await CurrentVersion(context), totalAmount = 0m, paidAmount = 999m, reservedAmount = 999m }, context.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual(999m, payload.RootElement.GetProperty("totalAmount").GetDecimal());
    }

    [Theory]
    [InlineData("awaiting_payment")]
    [InlineData("partially_paid")]
    [InlineData("paid")]
    public async Task CleaningCannotReleaseBeforeClosed(string sessionStatus)
    {
        var context = await fixture.CreateOpenSessionAsync();
        var tenantId = await TenantId(context);
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.Status = sessionStatus;
            if (sessionStatus is "partially_paid" or "paid") session.PaidAmount = 10m;
            var table = await db.Set<DiningTable>().SingleAsync(x => x.Id == session.DiningTableId); table.Status = "awaiting_cleaning";
            await db.SaveChangesAsync();
        }
        var token = await fixture.CreateUserTokenAsync(tenantId, "tables.cleaning.confirm");
        var tableId = await TableId(context);
        var response = await fixture.PostAsync($"api/v1/operations/tables/{tableId}/confirm-cleaning", null, token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SESSION_NOT_CLOSED", await fixture.ErrorCodeAsync(response));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal("awaiting_cleaning", (await verify.Set<DiningTable>().SingleAsync(x => x.Id == tableId)).Status);
    }

    [Fact]
    public async Task ClosedSessionPreservesCleaningReleaseFlow()
    {
        var context = await fixture.CreateOpenSessionAsync(); var tenantId = await TenantId(context); var tableId = await TableId(context);
        await using (var db = fixture.CreateDbContext()) { var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); session.Status = "closed"; var table = await db.Set<DiningTable>().SingleAsync(x => x.Id == tableId); table.Status = "awaiting_cleaning"; await db.SaveChangesAsync(); }
        var token = await fixture.CreateUserTokenAsync(tenantId, "tables.cleaning.confirm");
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.PostAsync($"api/v1/operations/tables/{tableId}/confirm-cleaning", null, token)).StatusCode);
        await using var verify = fixture.CreateDbContext(); Assert.Equal("available", (await verify.Set<DiningTable>().SingleAsync(x => x.Id == tableId)).Status);
    }

    [Fact]
    public async Task OrderWinsBeforeClosingAndClosingIncludesAcceptedOrder()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var productResponse = await fixture.PostAsync("api/v1/operations/catalog/products", new { productType = "simple", name = "Suco", description = (string?)null, internalCode = Guid.NewGuid().ToString("N"), primaryCategoryId = (Guid?)null, primaryImageMediaId = (Guid?)null, displayOrder = 1, requiresProduction = false, allowsNotes = true, maximumNoteLength = 120, preparationStationId = (Guid?)null }, tenant.AccessToken); productResponse.EnsureSuccessStatusCode();
        using var product = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync()); var productId = product.RootElement.GetProperty("id").GetGuid();
        var variantResponse = await fixture.PostAsync($"api/v1/operations/catalog/products/{productId}/variants", new { name = "500 ml", internalCode = Guid.NewGuid().ToString("N"), basePrice = 12.50m, imageMediaId = (Guid?)null, displayOrder = 1 }, tenant.AccessToken); variantResponse.EnsureSuccessStatusCode(); using var variant = JsonDocument.Parse(await variantResponse.Content.ReadAsStringAsync()); var variantId = variant.RootElement.GetProperty("id").GetGuid();
        (await fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true)).EnsureSuccessStatusCode(); await using (var stationDb = fixture.CreateDbContext()) { stationDb.Add(new Station { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Name = "Cozinha Geral", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await stationDb.SaveChangesAsync(); } var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var sessionId = await fixture.OpenSessionAsync(device.AccessToken);
        using var config = await fixture.GetJsonAsync($"api/v1/table-device/menu/products/{productId}", device.AccessToken); var localCartId = Guid.NewGuid(); var simulation = await fixture.PostAsync("api/v1/table-device/cart/simulate", new { sessionId, localCartId, catalogVersion = 1, availabilityVersion = 0, items = new[] { new { localCartItemId = Guid.NewGuid(), productId, productVariantId = variantId, quantity = 1, configurationVersion = config.RootElement.GetProperty("configurationVersion").GetString(), estimatedUnitAmount = 12.50m, configuration = new { } } } }, device.AccessToken); simulation.EnsureSuccessStatusCode(); using var sim = JsonDocument.Parse(await simulation.Content.ReadAsStringAsync());
        var order = await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", new { sessionId, localCartId, clientSubmissionId = Guid.NewGuid(), simulationId = sim.RootElement.GetProperty("simulationId").GetGuid(), simulationVersion = sim.RootElement.GetProperty("simulationVersion").GetString(), acceptedReview = false }, device.AccessToken, Guid.NewGuid()); Assert.True(order.StatusCode == HttpStatusCode.Created, await Diagnostic(order, "Order submission"));
        var close = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/close", new { sessionId, expectedVersion = await SessionVersion(sessionId) }, device.AccessToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        using var balance = JsonDocument.Parse(await (await fixture.GetAsync($"api/v1/table-device/session/balance?sessionId={sessionId}", device.AccessToken)).Content.ReadAsStringAsync()); Assert.Equal(12.50m, balance.RootElement.GetProperty("totalAmount").GetDecimal()); Assert.Equal("awaiting_payment", balance.RootElement.GetProperty("status").GetString());
        await using var verify = fixture.CreateDbContext(); Assert.Single(await verify.Set<Order>().Where(x => x.TableSessionId == sessionId).ToListAsync()); var closingEvents = await verify.OutboxMessages.Where(x => x.EventType == "session-closing-started.v1").ToListAsync(); Assert.Single(closingEvents, x => x.Payload.Contains(sessionId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpResponseMessage> Start(Phase1ApiFixture.OpenSessionContext context, long version, Guid? key = null)
    {
        if (version < 0) version = await CurrentVersion(context);
        return await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/close", new { sessionId = context.SessionId, expectedVersion = version }, context.Device.AccessToken, key ?? Guid.NewGuid());
    }

    private async Task<long> CurrentVersion(Phase1ApiFixture.OpenSessionContext context)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.Version).SingleAsync();
    }

    private async Task<long> SessionVersion(Guid sessionId)
    {
        await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(x => x.Id == sessionId).Select(x => x.Version).SingleAsync();
    }

    private async Task<Guid> TableId(Phase1ApiFixture.OpenSessionContext context)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.DiningTableId).SingleAsync();
    }

    private async Task<Guid> TenantId(Phase1ApiFixture.OpenSessionContext context)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.EstablishmentId).SingleAsync();
    }

    private static async Task<string> Diagnostic(HttpResponseMessage response, string operation)
    {
        var body = await response.Content.ReadAsStringAsync();
        return $"{operation} returned {(int)response.StatusCode} {response.StatusCode}. Body: {body}";
    }
}
