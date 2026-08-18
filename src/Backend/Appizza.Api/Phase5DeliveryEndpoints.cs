using System.Security.Claims;
using System.Text.Json;
using Appizza.Modules.Establishments;
using Appizza.Modules.Devices;
using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public sealed record SendToTableRequest(long? ExpectedVersion = null);
public sealed record DeliveryConfirmationRequest(string? Confirmation = null, long? ExpectedVersion = null);
public sealed record DeliveryContestationRequest(string? ReasonCode = null, string? CustomerNote = null, long? ExpectedVersion = null);
public sealed record DeliveryContestResolutionRequest(string Resolution, string? Reason = null, long? ExpectedVersion = null);

public static class Phase5DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapPhase5DeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/operations/kitchen/production-items/{id:guid}/send-to-table", SendToTable).RequireAuthorization();
        app.MapPost("/api/v1/table-device/order-items/{id:guid}/delivery-confirmation", ConfirmCustomer).RequireAuthorization();
        app.MapPost("/api/v1/operations/kitchen/delivery-confirmations/{id:guid}/confirm", ConfirmEmployee).RequireAuthorization();
        app.MapPost("/api/v1/table-device/order-items/{id:guid}/delivery-contestation", ContestCustomer).RequireAuthorization();
        app.MapPost("/api/v1/operations/kitchen/delivery-contests/{id:guid}/resolve", ResolveContest).RequireAuthorization();
        return app;
    }

    private static Task<IResult> ConfirmCustomer(Guid id, DeliveryConfirmationRequest? body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct) => Confirm(id, body, http, principal, db, false, hook, ct);
    private static Task<IResult> ConfirmEmployee(Guid id, DeliveryConfirmationRequest? body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct) => Confirm(id, body, http, principal, db, true, hook, ct);
    private static Task<IResult> ContestCustomer(Guid id, DeliveryContestationRequest? body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct) => Contest(id, body, http, principal, db, hook, ct);
    private static Task<IResult> ResolveContest(Guid id, DeliveryContestResolutionRequest body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct) => Resolve(id, body, http, principal, db, hook, ct);

    private static async Task<IResult> Resolve(Guid contestId, DeliveryContestResolutionRequest body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key UUID obrigatório.");
        if (body.Resolution is not ("confirm_delivered" or "retry_delivery")) return Error(400, "DELIVERY_INVALID_STATE", "Resolução inválida.");
        if (!principal.IsTokenType("user")) return Error(403, "INVALID_TOKEN_TYPE", "Token de funcionário necessário.");
        var tenant = principal.RequiredGuid("establishment_id"); var actor = principal.RequiredGuid("sub"); const string operation = "kitchen.delivery.resolve"; var hash = Phase4Pricing.Hash(JsonSerializer.Serialize(new { contestId, body.Resolution, body.Reason, body.ExpectedVersion }));
        await hook.ReachAsync("resolve-before-locks", contestId, body.Resolution, ct); await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N") + "|" + operation + "|" + key.ToString("N")}, 0))", ct);
        var replay = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct); if (replay is not null) { if (replay.RequestHash != hash) return Error(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", "A chave foi usada com outro request."); await tx.CommitAsync(ct); return Results.Content(replay.ResponsePayload!, "application/json", statusCode: replay.ResponseStatus); }
        var contest = await db.Set<DeliveryContest>().SingleOrDefaultAsync(x => x.Id == contestId && x.EstablishmentId == tenant, ct); if (contest is null) return Results.NotFound(); var productionId = contest.ProductionItemId;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE id = {productionId} AND establishment_id = {tenant} FOR UPDATE", ct); await hook.ReachAsync("resolve-after-production-item-lock", productionId, ct); db.ChangeTracker.Clear();
        var permissions = await PermissionResolver.ResolveAsync(db, actor, DateTimeOffset.UtcNow, ct); if (!permissions.Contains("kitchen.delivery.resolve")) return Error(403, "INSUFFICIENT_PERMISSION", "Permissão insuficiente.");
        var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == productionId && x.EstablishmentId == tenant, ct); var confirmation = await db.Set<DeliveryConfirmation>().SingleOrDefaultAsync(x => x.Id == contest.DeliveryConfirmationId && x.ProductionItemId == productionId && x.EstablishmentId == tenant, ct); if (confirmation is null) return Results.NotFound(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.delivery_confirmation WHERE id = {confirmation.Id} FOR UPDATE", ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.delivery_contest WHERE id = {contestId} FOR UPDATE", ct); db.ChangeTracker.Clear(); item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == productionId && x.EstablishmentId == tenant, ct); confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == contest.DeliveryConfirmationId, ct); contest = await db.Set<DeliveryContest>().SingleAsync(x => x.Id == contestId, ct);
        if (body.ExpectedVersion is long expected && expected != item.Version) return await Conflict(db, tx, tenant, key, operation, hash, "CONCURRENCY_CONFLICT", "Versão desatualizada.", ct); if (contest.Status != "open" || confirmation.Status != "contested") return await Conflict(db, tx, tenant, key, operation, hash, "DELIVERY_CONTEST_ALREADY_RESOLVED", "Contestação já resolvida.", ct); if (!permissions.Contains("kitchen.delivery.resolve")) return Error(403, "INSUFFICIENT_PERMISSION", "Permissão insuficiente.");
        var now = DateTimeOffset.UtcNow; contest.Status = body.Resolution == "confirm_delivered" ? "resolved_delivered" : "resolved_retry"; contest.Resolution = body.Resolution; contest.ResolutionNote = body.Reason; contest.ResolvedAt = now; contest.ResolvedByUserId = actor; contest.UpdatedAt = now; contest.Version++; item.Status = body.Resolution == "confirm_delivered" ? "delivered" : "ready"; item.UpdatedAt = now; if (body.Resolution == "retry_delivery") { confirmation.Status = "superseded"; confirmation.SupersededAt = now; confirmation.UpdatedAt = now; confirmation.Version++; }
        db.Add(new OutboxMessage { Id = Guid.NewGuid(), EstablishmentId = tenant, EventType = "delivery-contest-resolved.v1", SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventType = "DeliveryContestResolved", schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, actor = new { userId = actor }, data = new { productionItemId = productionId, deliveryConfirmationId = confirmation.Id, deliveryContestId = contest.Id, resolution = body.Resolution, version = item.Version } }) }); await db.SaveChangesAsync(ct); var payload = JsonSerializer.Serialize(new { productionItemId = productionId, productionStatus = item.Status, deliveryConfirmationId = confirmation.Id, deliveryStatus = confirmation.Status, deliveryContestId = contest.Id, resolution = contest.Resolution, version = item.Version, resolvedAt = contest.ResolvedAt }); db.Add(new IdempotencyRecord { EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = now }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static async Task<IResult> Contest(Guid orderItemId, DeliveryContestationRequest? body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key UUID obrigatório.");
        body ??= new();
        var device = await ValidateDevice(principal, db, ct); if (device.Error is not null) return device.Error;
        var tenant = device.Device!.EstablishmentId!.Value; var actor = device.Device.Id; const string operation = "kitchen.delivery.contest.customer";
        var hash = Phase4Pricing.Hash(JsonSerializer.Serialize(new { orderItemId, body.ReasonCode, body.CustomerNote, body.ExpectedVersion }));
        await hook.ReachAsync("contest-before-locks", orderItemId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N") + "|" + operation + "|" + key.ToString("N")}, 0))", ct);
        var replay = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (replay is not null) { if (replay.RequestHash != hash) return Error(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", "A chave foi usada com outro request."); await tx.CommitAsync(ct); return Results.Content(replay.ResponsePayload!, "application/json", statusCode: replay.ResponseStatus); }
        var productionId = await db.Set<ProductionItem>().Where(x => x.EstablishmentId == tenant && x.OrderItemId == orderItemId).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (productionId == Guid.Empty) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE id = {productionId} AND establishment_id = {tenant} FOR UPDATE", ct);
        await hook.ReachAsync("contest-after-production-item-lock", productionId, ct);
        db.ChangeTracker.Clear();
        var currentDevice = await ValidateDevice(principal, db, ct); if (currentDevice.Error is not null) return currentDevice.Error;
        var binding = await db.Set<DeviceTableBinding>().SingleOrDefaultAsync(x => x.DeviceId == actor && x.UnboundAt == null, ct);
        var order = await db.Set<Order>().SingleOrDefaultAsync(x => x.Id == db.Set<OrderItem>().Where(i => i.Id == orderItemId).Select(i => i.OrderId).SingleOrDefault(), ct);
        if (binding is null || order is null || order.TableSessionId == Guid.Empty || !await db.Set<TableSession>().AnyAsync(s => s.Id == order.TableSessionId && s.DiningTableId == binding.DiningTableId && s.EstablishmentId == tenant && s.Status == "open", ct)) return Results.NotFound();
        var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == productionId && x.EstablishmentId == tenant, ct);
        var confirmation = await db.Set<DeliveryConfirmation>().SingleOrDefaultAsync(x => x.ProductionItemId == productionId && x.EstablishmentId == tenant && x.Status == "confirmed_automatic", ct);
        if (confirmation is null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.delivery_confirmation WHERE id = {confirmation.Id} FOR UPDATE", ct); db.ChangeTracker.Clear();
        item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == productionId && x.EstablishmentId == tenant, ct);
        confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == confirmation.Id, ct);
        if (body.ExpectedVersion is long expected && expected != item.Version) return await Conflict(db, tx, tenant, key, operation, hash, "CONCURRENCY_CONFLICT", "Versão desatualizada.", ct);
        var minutes = await Setting(db, tenant, Phase1SettingKeys.DeliveryAutoContestationWindowMinutes, 5, ct);
        var deadline = confirmation.ConfirmedAt?.AddMinutes(minutes);
        var now = DateTimeOffset.UtcNow;
        if (item.Status != "delivered" || confirmation.Status != "confirmed_automatic") return await Conflict(db, tx, tenant, key, operation, hash, "DELIVERY_INVALID_STATE", "Entrega não pode ser contestada.", ct);
        if (deadline is null || now > deadline.Value) return await Conflict(db, tx, tenant, key, operation, hash, "DELIVERY_CONTESTATION_WINDOW_EXPIRED", "Janela de contestação expirada.", ct);
        if (await db.Set<DeliveryContest>().AnyAsync(x => x.ProductionItemId == productionId && x.Status == "open", ct)) return await Conflict(db, tx, tenant, key, operation, hash, "DELIVERY_ALREADY_CONTESTED", "A entrega já está contestada.", ct);
        var contest = new DeliveryContest { Id = Guid.NewGuid(), EstablishmentId = tenant, DeliveryConfirmationId = confirmation.Id, ProductionItemId = productionId, Status = "open", Version = 1, OpenedAt = now, OpenedByDeviceId = actor, ResolutionNote = body.CustomerNote, CreatedAt = now, UpdatedAt = now };
        confirmation.Status = "contested"; confirmation.ContestedAt = now; confirmation.UpdatedAt = now; confirmation.Version++; item.Status = "awaiting_delivery_confirmation"; item.UpdatedAt = now;
        db.Add(contest); db.Add(new OutboxMessage { Id = Guid.NewGuid(), EstablishmentId = tenant, EventType = "delivery-contested.v1", SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventType = "DeliveryContested", schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, actor = new { deviceId = actor }, data = new { productionItemId = productionId, deliveryConfirmationId = confirmation.Id, deliveryContestId = contest.Id, version = item.Version, contestedAt = now } }) });
        await db.SaveChangesAsync(ct); var payload = JsonSerializer.Serialize(new { productionItemId = productionId, productionStatus = item.Status, deliveryConfirmationId = confirmation.Id, deliveryStatus = confirmation.Status, deliveryContestId = contest.Id, version = item.Version, contestedAt = now }); db.Add(new IdempotencyRecord { EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = now }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static async Task<IResult> Confirm(Guid id, DeliveryConfirmationRequest? body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, bool employee, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key UUID é obrigatório.");
        body ??= new(); if (!employee && body.Confirmation != "received") return Error(400, "INVALID_CONFIRMATION", "Confirmação inválida.");
        var operation = employee ? "kitchen.delivery.confirm.employee" : "kitchen.delivery.confirm.customer";
        var hash = Phase4Pricing.Hash(JsonSerializer.Serialize(new { id, body.ExpectedVersion, body.Confirmation }));
        Guid tenant; Guid actor;
        if (employee)
        {
            if (!principal.IsTokenType("user")) return Error(403, "INVALID_TOKEN_TYPE", "Token de funcionário necessário.");
            tenant = principal.RequiredGuid("establishment_id"); actor = principal.RequiredGuid("sub");
        }
        else
        {
            var device = await ValidateDevice(principal, db, ct); if (device.Error is not null) return device.Error;
            tenant = device.Device!.EstablishmentId!.Value; actor = device.Device.Id;
        }
        await hook.ReachAsync(employee ? "employee-before-locks" : "customer-before-locks", id, ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N") + "|" + operation + "|" + key.ToString("N")}, 0))", ct);
        var replay = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (replay is not null) { if (replay.RequestHash != hash) return Error(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", "A chave foi usada com outro request."); await tx.CommitAsync(ct); return Results.Content(replay.ResponsePayload!, replay.ResponseStatus == 409 ? "application/problem+json" : "application/json", statusCode: replay.ResponseStatus); }
        Guid productionId;
        if (employee) productionId = await db.Set<DeliveryConfirmation>().Where(x => x.Id == id && x.EstablishmentId == tenant).Select(x => x.ProductionItemId).SingleOrDefaultAsync(ct);
        else productionId = await db.Set<ProductionItem>().Where(x => x.EstablishmentId == tenant && x.OrderItemId == id).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (productionId == Guid.Empty) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE id = {productionId} AND establishment_id = {tenant} FOR UPDATE", ct);
        await hook.ReachAsync(employee ? "employee-after-locks" : "customer-after-locks", productionId, ct);
        db.ChangeTracker.Clear();
        if (employee)
        {
            var permissions = await PermissionResolver.ResolveAsync(db, actor, DateTimeOffset.UtcNow, ct); if (!permissions.Contains("kitchen.delivery.confirm")) return Error(403, "INSUFFICIENT_PERMISSION", "Permissão insuficiente.");
        }
        else
        {
            var binding = await db.Set<DeviceTableBinding>().SingleOrDefaultAsync(x => x.DeviceId == actor && x.UnboundAt == null, ct);
            var order = await db.Set<Order>().SingleOrDefaultAsync(x => x.Id == db.Set<OrderItem>().Where(i => i.Id == id).Select(i => i.OrderId).SingleOrDefault(), ct);
            if (binding is null || order is null || order.TableSessionId == Guid.Empty || !await db.Set<TableSession>().AnyAsync(s => s.Id == order.TableSessionId && s.DiningTableId == binding.DiningTableId && s.EstablishmentId == tenant && s.Status == "open", ct)) return Results.NotFound();
        }
        var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == productionId && x.EstablishmentId == tenant, ct);
        var confirmation = employee ? await db.Set<DeliveryConfirmation>().SingleOrDefaultAsync(x => x.Id == id && x.ProductionItemId == item.Id && x.EstablishmentId == tenant, ct) : await db.Set<DeliveryConfirmation>().SingleOrDefaultAsync(x => x.ProductionItemId == item.Id && x.EstablishmentId == tenant && x.Status == "pending", ct);
        if (confirmation is null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.delivery_confirmation WHERE id = {confirmation.Id} FOR UPDATE", ct); db.ChangeTracker.Clear();
        item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == productionId && x.EstablishmentId == tenant, ct); confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == confirmation.Id, ct);
        if (body.ExpectedVersion is long expected && expected != item.Version) return await Conflict(db, tx, tenant, key, operation, hash, "CONCURRENCY_CONFLICT", "Versão desatualizada.", ct);
        if (item.Status != "awaiting_delivery_confirmation" || confirmation.Status != "pending") return await Conflict(db, tx, tenant, key, operation, hash, "DELIVERY_ALREADY_CONFIRMED", "Entrega já confirmada ou indisponível.", ct);
        var now = DateTimeOffset.UtcNow; confirmation.Status = "confirmed_manual"; confirmation.ConfirmedAt = now; confirmation.ConfirmationSource = employee ? "employee" : "customer"; confirmation.ConfirmedByUserId = employee ? actor : null; confirmation.ConfirmedByDeviceId = employee ? null : actor; confirmation.UpdatedAt = now; confirmation.Version++; item.Status = "delivered"; item.UpdatedAt = now;
        AddManualEvent(db, tenant, employee ? "delivery-confirmed-by-employee.v1" : "delivery-confirmed-by-customer.v1", employee ? "DeliveryConfirmedByEmployee" : "DeliveryConfirmedByCustomer", item, confirmation, now, employee ? actor : null, employee ? null : actor);
        AddManualEvent(db, tenant, "production-item-delivered.v1", "ProductionItemDelivered", item, confirmation, now, employee ? actor : null, employee ? null : actor);
        await db.SaveChangesAsync(ct); var payload = JsonSerializer.Serialize(new { productionItemId = item.Id, deliveryConfirmationId = confirmation.Id, status = item.Status, confirmationStatus = confirmation.Status, version = item.Version, confirmedAt = confirmation.ConfirmedAt }); db.Add(new IdempotencyRecord { EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = now }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static void AddManualEvent(AppizzaDbContext db, Guid tenant, string type, string name, ProductionItem item, DeliveryConfirmation confirmation, DateTimeOffset now, Guid? user, Guid? device) { var eventId = Guid.NewGuid(); db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = type, SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventId, eventType = name, schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, actor = new { userId = user, deviceId = device }, data = new { productionItemId = item.Id, deliveryConfirmationId = confirmation.Id, sequence = confirmation.SequenceNumber } }) }); }
    private static async Task<(Device? Device, IResult? Error)> ValidateDevice(ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) { if (!p.IsTokenType("device")) return (null, Error(403, "INVALID_TOKEN_TYPE", "Token de dispositivo necessário.")); var tenant = p.RequiredGuid("establishment_id"); var id = p.RequiredGuid("sub"); var device = await db.Set<Device>().SingleOrDefaultAsync(x => x.Id == id && x.EstablishmentId == tenant, ct); if (device is null) return (null, Results.NotFound()); if (device.Status == "blocked") return (null, Error(403, "DEVICE_BLOCKED", "Dispositivo bloqueado.")); if (device.Status != "active" || device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != p.FindFirstValue("credential_version")) return (null, Error(403, "DEVICE_CREDENTIAL_REVOKED", "Credencial revogada.")); return (device, null); }

    private static async Task<IResult> SendToTable(Guid id, SendToTableRequest? body, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, IPhase5DeliveryConcurrencyHook hook, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key UUID é obrigatório.");
        body ??= new();
        if (!principal.IsTokenType("user")) return Error(403, "INVALID_TOKEN_TYPE", "Token de funcionário necessário.");
        var tenant = principal.RequiredGuid("establishment_id"); var user = principal.RequiredGuid("sub");
        const string operation = "kitchen.delivery.send";
        var hash = Phase4Pricing.Hash(JsonSerializer.Serialize(new { id, body.ExpectedVersion }));
        await hook.ReachAsync("send-before-locks", id, "send_to_table", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N") + "|" + operation + "|" + key.ToString("N")}, 0))", ct);
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) return Error(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", "A chave foi usada com outro request.");
            await tx.CommitAsync(ct); return Results.Content(existing.ResponsePayload!, existing.ResponseStatus == 409 ? "application/problem+json" : "application/json", statusCode: existing.ResponseStatus);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE id = {id} AND establishment_id = {tenant} FOR UPDATE", ct);
        await hook.ReachAsync("send-after-production-item-lock", id, "send_to_table", ct);
        db.ChangeTracker.Clear();
        var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct);
        if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound();
        if (!permissions.Contains("kitchen.delivery.send")) return Error(403, "INSUFFICIENT_PERMISSION", "Permissão insuficiente.");
        var item = await db.Set<ProductionItem>().SingleOrDefaultAsync(x => x.Id == id && x.EstablishmentId == tenant, ct);
        if (item is null) return Results.NotFound();
        if (body.ExpectedVersion is long expected && expected != item.Version) return await Conflict(db, tx, tenant, key, operation, hash, "CONCURRENCY_CONFLICT", "Versão desatualizada.", ct);
        if (item.Status != "ready") return await Conflict(db, tx, tenant, key, operation, hash, "PRODUCTION_ITEM_NOT_READY", "Somente itens prontos podem ser enviados à mesa.", ct);
        var now = DateTimeOffset.UtcNow;
        var enabled = await Setting(db, tenant, Phase1SettingKeys.DeliveryAutoConfirmationEnabled, true, ct);
        var minutes = await Setting(db, tenant, Phase1SettingKeys.DeliveryAutoConfirmationMinutes, 5, ct);
        var nextSequence = (await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == item.Id).Select(x => (int?)x.SequenceNumber).MaxAsync(ct) ?? 0) + 1;
        var confirmation = new DeliveryConfirmation { Id = Guid.NewGuid(), EstablishmentId = tenant, ProductionItemId = item.Id, SequenceNumber = nextSequence, Status = "pending", Version = 1, RequestedAt = now, ExpiresAt = enabled ? now.AddMinutes(minutes) : now.AddMinutes(minutes), CreatedAt = now, UpdatedAt = now };
        item.Status = "awaiting_delivery_confirmation"; item.UpdatedAt = now;
        db.Add(confirmation);
        AddEvent(db, tenant, "production-item-sent-to-table.v1", "ProductionItemSentToTable", item, confirmation, now, user);
        AddEvent(db, tenant, "delivery-confirmation-requested.v1", "DeliveryConfirmationRequested", item, confirmation, now, user);
        await db.SaveChangesAsync(ct);
        var payload = JsonSerializer.Serialize(new { productionItemId = item.Id, status = item.Status, version = item.Version, deliveryConfirmationId = confirmation.Id, sequence = confirmation.SequenceNumber, confirmationStatus = confirmation.Status, requestedAt = confirmation.RequestedAt, expiresAt = confirmation.ExpiresAt });
        db.Add(new IdempotencyRecord { EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static void AddEvent(AppizzaDbContext db, Guid tenant, string type, string name, ProductionItem item, DeliveryConfirmation confirmation, DateTimeOffset now, Guid user)
    {
        var eventId = Guid.NewGuid(); db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = type, SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventId, eventType = name, schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, actor = new { userId = user }, data = new { productionItemId = item.Id, deliveryConfirmationId = confirmation.Id, sequence = confirmation.SequenceNumber } }) });
    }
    private static async Task<int> Setting(AppizzaDbContext db, Guid tenant, string key, int fallback, CancellationToken ct) { var value = await db.Set<EstablishmentSetting>().Where(x => x.EstablishmentId == tenant && x.SettingKey == key).Select(x => x.SettingValue).SingleOrDefaultAsync(ct); return int.TryParse(value, out var result) ? result : fallback; }
    private static async Task<bool> Setting(AppizzaDbContext db, Guid tenant, string key, bool fallback, CancellationToken ct) { var value = await db.Set<EstablishmentSetting>().Where(x => x.EstablishmentId == tenant && x.SettingKey == key).Select(x => x.SettingValue).SingleOrDefaultAsync(ct); return bool.TryParse(value, out var result) ? result : fallback; }
    private static async Task<IResult> Conflict(AppizzaDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Guid tenant, Guid key, string operation, string hash, string code, string detail, CancellationToken ct) { var payload = JsonSerializer.Serialize(new { type = "about:blank", title = detail, status = 409, detail, errorCode = code }); db.Add(new IdempotencyRecord { EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 409, ResponsePayload = payload, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/problem+json", statusCode: 409); }
    private static IResult Error(int status, string code, string detail) => Results.Problem(statusCode: status, title: detail, detail: detail, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}




