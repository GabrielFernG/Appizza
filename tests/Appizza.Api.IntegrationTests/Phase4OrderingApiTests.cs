using System.Net;
using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase4OrderingApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task SimulationAndConcurrentReplayCreateOneHistoricalOrder()
    {
        var context = await CreateContext();
        using var config = await fixture.GetJsonAsync($"api/v1/table-device/menu/products/{context.ProductId}", context.DeviceToken);
        var localCartId = Guid.NewGuid();
        var request = new { sessionId = context.SessionId, localCartId, catalogVersion = 1, availabilityVersion = 0, items = new[] { new { localCartItemId = Guid.NewGuid(), productId = context.ProductId, productVariantId = context.VariantId, quantity = 2, configurationVersion = config.RootElement.GetProperty("configurationVersion").GetString(), estimatedUnitAmount = 0.01m, configuration = new { } } } };
        var simulatedResponse = await fixture.PostAsync("api/v1/table-device/cart/simulate", request, context.DeviceToken); simulatedResponse.EnsureSuccessStatusCode(); using var simulated = JsonDocument.Parse(await simulatedResponse.Content.ReadAsStringAsync()); Assert.Equal(25m, simulated.RootElement.GetProperty("totals").GetProperty("totalAmount").GetDecimal()); Assert.True(simulated.RootElement.GetProperty("requiresReview").GetBoolean());
        var submissionId = Guid.NewGuid(); var key = Guid.NewGuid(); var submission = new { sessionId = context.SessionId, localCartId, clientSubmissionId = submissionId, simulationId = simulated.RootElement.GetProperty("simulationId").GetGuid(), simulationVersion = simulated.RootElement.GetProperty("simulationVersion").GetString(), acceptedReview = true };
        var responses = await fixture.ConcurrentAsync(() => fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", submission, context.DeviceToken, key), () => fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", submission, context.DeviceToken, key)); Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        await using var db = fixture.CreateDbContext(); var order = await db.Set<Order>().SingleAsync(x => x.ClientSubmissionId == submissionId); Assert.Equal(25m, order.TotalAmount); var item = await db.Set<OrderItem>().SingleAsync(x => x.OrderId == order.Id); Assert.Contains("snapshotSchemaVersion", item.Snapshot); Assert.Contains("ConfigurationVersion", item.Snapshot); var outbox = await db.OutboxMessages.Where(x => x.EventType == "order-submitted.v1").Select(x => x.Payload).ToListAsync(); Assert.Single(outbox, x => x.Contains(order.Id.ToString(), StringComparison.OrdinalIgnoreCase)); Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.OperationType == "ordering.submit" && x.IdempotencyKey == key.ToString()));
    }

    [Fact]
    public async Task TenantBoundaryAndDifferentIdempotentPayloadAreRejected()
    {
        var first = await CreateContext(); var second = await CreateContext(); var crossTenant = await fixture.PostAsync("api/v1/table-device/cart/simulate", new { sessionId = second.SessionId, localCartId = Guid.NewGuid(), catalogVersion = 1, availabilityVersion = 0, items = Array.Empty<object>() }, first.DeviceToken); Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        await using var db = fixture.CreateDbContext(); db.Add(new IdempotencyRecord { EstablishmentId = first.EstablishmentId, IdempotencyKey = Guid.Empty.ToString(), OperationType = "ordering.submit", RequestHash = "different", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); var conflict = await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", new { sessionId = first.SessionId, localCartId = Guid.NewGuid(), clientSubmissionId = Guid.NewGuid(), simulationId = Guid.NewGuid(), simulationVersion = "x", acceptedReview = false }, first.DeviceToken, Guid.Empty); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(conflict));
    }

    [Fact]
    public async Task ConcurrentDifferentKeysForSameClientSubmissionCreateExactlyOneOrderAndEvent()
    {
        var context = await CreateContext(); var seed = await CreateSubmission(context); var firstKey = Guid.NewGuid(); var secondKey = Guid.NewGuid();
        var responses = await fixture.ConcurrentAsync(
            () => fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", seed.Request, context.DeviceToken, firstKey),
            () => fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", seed.Request, context.DeviceToken, secondKey));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        using var first = JsonDocument.Parse(await responses[0].Content.ReadAsStringAsync()); using var second = JsonDocument.Parse(await responses[1].Content.ReadAsStringAsync());
        Assert.Equal(first.RootElement.GetProperty("order").GetProperty("id").GetGuid(), second.RootElement.GetProperty("order").GetProperty("id").GetGuid());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<Order>().CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.ClientSubmissionId == seed.ClientSubmissionId));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.EventType == "order-submitted.v1"));
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.OperationType == "ordering.submit"));
        var connection = db.Database.GetDbConnection(); if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "select count(*) from pg_indexes where schemaname='ordering' and tablename='customer_order' and indexdef ilike '%establishment_id%source_device_id%client_submission_id%'"; Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task IdempotencyKeyIsTenantAndOperationAware()
    {
        var first = await CreateContext(); var second = await CreateContext(); var firstSeed = await CreateSubmission(first); var secondSeed = await CreateSubmission(second); var key = Guid.NewGuid();
        var responses = await Task.WhenAll(
            fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", firstSeed.Request, first.DeviceToken, key),
            fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", secondSeed.Request, second.DeviceToken, key));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync(x => x.IdempotencyKey == key.ToString() && x.OperationType == "ordering.submit"));
        db.Add(new IdempotencyRecord { EstablishmentId = first.EstablishmentId, IdempotencyKey = key.ToString(), OperationType = "kitchen.accept", RequestHash = "independent", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == first.EstablishmentId && x.IdempotencyKey == key.ToString()));
        var crossTenantReconcile = await fixture.GetAsync($"api/v1/table-device/orders/submissions/{key}", second.DeviceToken); crossTenantReconcile.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await crossTenantReconcile.Content.ReadAsStringAsync()); Assert.Equal((await db.Set<Order>().SingleAsync(x => x.EstablishmentId == second.EstablishmentId)).Id, payload.RootElement.GetProperty("order").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ConcurrentOrdersFromTwoDevicesDoNotLoseSessionTotals()
    {
        var context = await CreateContext();
        await using var db = fixture.CreateDbContext(); var tenant = context.EstablishmentId;
        var session = await db.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == context.SessionId); var tableId = session.DiningTableId;
        var secondDevice = await fixture.RegisterAndBindAsync(context.UserToken, tableId);
        var secondContext = context with { DeviceToken = secondDevice.AccessToken }; var firstSeed = await CreateSubmission(context); var secondSeed = await CreateSubmission(secondContext);
        var responses = await fixture.ConcurrentAsync(
            () => fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", firstSeed.Request, context.DeviceToken, Guid.NewGuid()),
            () => fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", secondSeed.Request, secondContext.DeviceToken, Guid.NewGuid()));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)); db.ChangeTracker.Clear();
        var ordersTotal = await db.Set<Order>().Where(x => x.EstablishmentId == tenant && x.TableSessionId == context.SessionId).SumAsync(x => x.TotalAmount); var persisted = await db.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == context.SessionId);
        Assert.Equal(ordersTotal, persisted.SubtotalAmount); Assert.Equal(ordersTotal, persisted.TotalAmount); Assert.Equal(ordersTotal, persisted.RemainingAmount); Assert.Equal(0, await db.Set<Order>().Where(x => x.TableSessionId == context.SessionId).SumAsync(x => x.DiscountAmount));
    }

    [Fact]
    public async Task LostResponseIsRecoveredAndPostReplayNeverCreatesAnotherOrder()
    {
        var context = await CreateContext(); var seed = await CreateSubmission(context); var key = Guid.NewGuid();
        using (var ignoredResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", seed.Request, context.DeviceToken, key)) Assert.Equal(HttpStatusCode.Created, ignoredResponse.StatusCode);
        var reconciliation = await fixture.GetAsync($"api/v1/table-device/orders/submissions/{key}", context.DeviceToken); reconciliation.EnsureSuccessStatusCode(); using var recovered = JsonDocument.Parse(await reconciliation.Content.ReadAsStringAsync()); var recoveredId = recovered.RootElement.GetProperty("order").GetProperty("id").GetGuid();
        var replay = await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", seed.Request, context.DeviceToken, key); Assert.Equal(HttpStatusCode.Created, replay.StatusCode); using var replayed = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()); Assert.Equal(recoveredId, replayed.RootElement.GetProperty("order").GetProperty("id").GetGuid());
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<Order>().CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.ClientSubmissionId == seed.ClientSubmissionId)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.EventType == "order-submitted.v1"));
    }

    [Fact]
    public async Task ClosingAndSubmissionAreSerializedInBothLockOrders()
    {
        var closingFirst = await CreateContext(); var rejectedSeed = await CreateSubmission(closingFirst); var before = fixture.OrderingHook.Pause("submission-before-locks");
        var rejectedTask = fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", rejectedSeed.Request, closingFirst.DeviceToken, Guid.NewGuid()); await before.Reached;
        await using (var db = fixture.CreateDbContext()) { var session = await db.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == closingFirst.SessionId); session.Status = "closing"; await db.SaveChangesAsync(); } before.Release();
        var rejected = await rejectedTask; Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); Assert.Equal("SESSION_NOT_OPEN", await fixture.ErrorCodeAsync(rejected));
        await AssertNoCommercialEffects(closingFirst);

        var orderingFirst = await CreateContext(); var acceptedSeed = await CreateSubmission(orderingFirst); var locked = fixture.OrderingHook.Pause("submission-locks-acquired"); var acceptedTask = fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", acceptedSeed.Request, orderingFirst.DeviceToken, Guid.NewGuid()); await locked.Reached;
        var closingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var closeTask = Task.Run(async () => { await using var db = fixture.CreateDbContext(); closingStarted.SetResult(); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tables.table_session SET status = 'closing' WHERE id = {orderingFirst.SessionId}"); }); await closingStarted.Task; locked.Release();
        var accepted = await acceptedTask; await closeTask; Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        await using var verify = fixture.CreateDbContext(); var order = await verify.Set<Order>().SingleAsync(x => x.EstablishmentId == orderingFirst.EstablishmentId); Assert.Single(await verify.Set<OrderItem>().Where(x => x.OrderId == order.Id).ToListAsync()); Assert.Equal(1, await verify.OutboxMessages.CountAsync(x => x.EstablishmentId == orderingFirst.EstablishmentId && x.EventType == "order-submitted.v1")); var finalSession = await verify.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == orderingFirst.SessionId); Assert.Equal("closing", finalSession.Status); Assert.Equal(order.TotalAmount, finalSession.TotalAmount);
    }

    private async Task AssertNoCommercialEffects(Context context)
    { await using var db = fixture.CreateDbContext(); Assert.Equal(0, await db.Set<Order>().CountAsync(x => x.EstablishmentId == context.EstablishmentId)); Assert.Equal(0, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.EventType == "order-submitted.v1")); Assert.Equal(0, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == context.EstablishmentId && x.OperationType == "ordering.submit")); var session = await db.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == context.SessionId); Assert.Equal(0, session.TotalAmount); }

    [Theory]
    [InlineData("revoke-configuration")]
    [InlineData("block")]
    public async Task DeviceMutationAndSubmissionAreSerializedInBothLockOrders(string action)
    {
        var mutationFirst = await CreateContext(); var rejectedSeed = await CreateSubmission(mutationFirst); Guid firstDevice; await using (var db = fixture.CreateDbContext()) firstDevice = await db.Set<Appizza.Modules.Devices.Device>().Where(x => x.EstablishmentId == mutationFirst.EstablishmentId && x.Status == "active").Select(x => x.Id).SingleAsync(); var paused = fixture.OrderingHook.Pause("submission-before-locks"); var rejectedTask = fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", rejectedSeed.Request, mutationFirst.DeviceToken, Guid.NewGuid()); await paused.Reached; Assert.Equal(HttpStatusCode.NoContent, (await fixture.PostAsync($"api/v1/operations/table-devices/{firstDevice}/{action}", null, mutationFirst.UserToken)).StatusCode); paused.Release(); var rejected = await rejectedTask; Assert.True(rejected.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound); await AssertNoCommercialEffects(mutationFirst);

        var orderingFirst = await CreateContext(); var acceptedSeed = await CreateSubmission(orderingFirst); Guid secondDevice; await using (var db = fixture.CreateDbContext()) secondDevice = await db.Set<Appizza.Modules.Devices.Device>().Where(x => x.EstablishmentId == orderingFirst.EstablishmentId && x.Status == "active").Select(x => x.Id).SingleAsync(); var locked = fixture.OrderingHook.Pause("submission-locks-acquired"); var acceptedTask = fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", acceptedSeed.Request, orderingFirst.DeviceToken, Guid.NewGuid()); await locked.Reached; var mutationTask = fixture.PostAsync($"api/v1/operations/table-devices/{secondDevice}/{action}", null, orderingFirst.UserToken); locked.Release(); Assert.Equal(HttpStatusCode.Created, (await acceptedTask).StatusCode); Assert.Equal(HttpStatusCode.NoContent, (await mutationTask).StatusCode); Assert.NotEqual(HttpStatusCode.OK, (await fixture.GetAsync("api/v1/table-devices/me", orderingFirst.DeviceToken)).StatusCode); await using var verify = fixture.CreateDbContext(); Assert.Equal(1, await verify.Set<Order>().CountAsync(x => x.EstablishmentId == orderingFirst.EstablishmentId)); Assert.Equal(1, await verify.OutboxMessages.CountAsync(x => x.EstablishmentId == orderingFirst.EstablishmentId && x.EventType == "order-submitted.v1")); Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x => x.EstablishmentId == orderingFirst.EstablishmentId && x.OperationType == "ordering.submit"));
    }

    [Fact]
    public async Task ForeignProductVariantAndSubmissionAreNotDisclosedOrPersisted()
    {
        var tenantA = await CreateContext(); var tenantB = await CreateContext(); using var foreignConfig = await fixture.GetJsonAsync($"api/v1/table-device/menu/products/{tenantB.ProductId}", tenantB.DeviceToken);
        var simulate = await fixture.PostAsync("api/v1/table-device/cart/simulate", new { sessionId = tenantA.SessionId, localCartId = Guid.NewGuid(), catalogVersion = 1, availabilityVersion = 0, items = new[] { new { localCartItemId = Guid.NewGuid(), productId = tenantB.ProductId, productVariantId = tenantB.VariantId, quantity = 1, configurationVersion = foreignConfig.RootElement.GetProperty("configurationVersion").GetString(), estimatedUnitAmount = 12.50m, configuration = new { } } } }, tenantA.DeviceToken);
        Assert.Equal(HttpStatusCode.OK, simulate.StatusCode); using (var rejected = JsonDocument.Parse(await simulate.Content.ReadAsStringAsync())) { Assert.False(rejected.RootElement.GetProperty("canSubmit").GetBoolean()); Assert.Contains(rejected.RootElement.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("errorCode").GetString() == "PRODUCT_CONFIGURATION_CHANGED"); } await using var db = fixture.CreateDbContext(); Assert.Equal(0, await db.Set<Order>().CountAsync(x => x.EstablishmentId == tenantB.EstablishmentId)); Assert.Equal(0, await db.Set<CartSimulation>().CountAsync(x => x.EstablishmentId == tenantB.EstablishmentId && x.SourceDeviceId != Guid.Empty));
        var bSeed = await CreateSubmission(tenantB); var bKey = Guid.NewGuid(); (await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", bSeed.Request, tenantB.DeviceToken, bKey)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await fixture.GetAsync($"api/v1/table-device/orders/submissions/{bKey}", tenantA.DeviceToken)).StatusCode);
    }

    private async Task<SubmissionSeed> CreateSubmission(Context context)
    {
        using var config = await fixture.GetJsonAsync($"api/v1/table-device/menu/products/{context.ProductId}", context.DeviceToken); var localCartId = Guid.NewGuid(); var clientSubmissionId = Guid.NewGuid();
        var simulationRequest = new { sessionId = context.SessionId, localCartId, catalogVersion = 1, availabilityVersion = 0, items = new[] { new { localCartItemId = Guid.NewGuid(), productId = context.ProductId, productVariantId = context.VariantId, quantity = 1, configurationVersion = config.RootElement.GetProperty("configurationVersion").GetString(), estimatedUnitAmount = 12.50m, configuration = new { } } } };
        var response = await fixture.PostAsync("api/v1/table-device/cart/simulate", simulationRequest, context.DeviceToken); response.EnsureSuccessStatusCode(); using var simulation = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        object request = new { sessionId = context.SessionId, localCartId, clientSubmissionId, simulationId = simulation.RootElement.GetProperty("simulationId").GetGuid(), simulationVersion = simulation.RootElement.GetProperty("simulationVersion").GetString(), acceptedReview = false };
        return new(clientSubmissionId, request);
    }

    private async Task<Context> CreateContext()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var productResponse = await fixture.PostAsync("api/v1/operations/catalog/products", new { productType = "simple", name = "Suco", description = (string?)null, internalCode = Guid.NewGuid().ToString("N"), primaryCategoryId = (Guid?)null, primaryImageMediaId = (Guid?)null, displayOrder = 1, requiresProduction = false, allowsNotes = true, maximumNoteLength = 120, preparationStationId = (Guid?)null }, tenant.AccessToken); productResponse.EnsureSuccessStatusCode(); using var product = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync()); var productId = product.RootElement.GetProperty("id").GetGuid(); var variantResponse = await fixture.PostAsync($"api/v1/operations/catalog/products/{productId}/variants", new { name = "500 ml", internalCode = Guid.NewGuid().ToString("N"), basePrice = 12.50m, imageMediaId = (Guid?)null, displayOrder = 1 }, tenant.AccessToken); variantResponse.EnsureSuccessStatusCode(); using var variant = JsonDocument.Parse(await variantResponse.Content.ReadAsStringAsync()); var variantId = variant.RootElement.GetProperty("id").GetGuid(); (await fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true)).EnsureSuccessStatusCode(); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); await using var db = fixture.CreateDbContext(); db.Add(new Station { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Name = "Cozinha Geral", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); return new(tenant.EstablishmentId, tenant.AccessToken, device.AccessToken, session, productId, variantId);
    }
    private sealed record Context(Guid EstablishmentId, string UserToken, string DeviceToken, Guid SessionId, Guid ProductId, Guid VariantId);
    private sealed record SubmissionSeed(Guid ClientSubmissionId, object Request);
}
