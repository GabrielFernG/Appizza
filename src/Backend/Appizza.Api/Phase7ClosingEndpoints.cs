using System.Security.Claims;
using System.Text.Json;
using Appizza.Modules.Devices;
using Appizza.Modules.Auditing;
using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase7ClosingEndpoints
{
    public static IEndpointRouteBuilder MapPhase7ClosingEndpoints(this IEndpointRouteBuilder app)
    {
        var device = app.MapGroup("/api/v1/table-device/session").RequireAuthorization();
        device.MapPost("/close", StartClosing);
        device.MapGet("/balance", DeviceBalance);
        var operations = app.MapGroup("/api/v1/operations/sessions").RequireAuthorization();
        operations.MapPost("/{id:guid}/closing/cancel", CancelClosing);
        operations.MapGet("/{id:guid}/balance", OperationsBalance);
        return app;
    }

    private static async Task<IResult> StartClosing(ClosingRequest request, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("device")) return Problem(403, "INVALID_TOKEN_TYPE");
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Problem(400, "IDEMPOTENCY_KEY_REQUIRED");
        var tenant = principal.RequiredGuid("establishment_id"); var deviceId = principal.RequiredGuid("sub");
        var currentDevice = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId && x.EstablishmentId == tenant, ct);
        if (currentDevice is null) return Results.NotFound();
        if (currentDevice.Status == "blocked") return Problem(403, "DEVICE_BLOCKED");
        if (currentDevice.Status != "active" || currentDevice.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != principal.FindFirstValue("credential_version")) return Problem(403, "DEVICE_CREDENTIAL_REVOKED");
        var binding = await db.Set<DeviceTableBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.UnboundAt == null, ct);
        if (binding is null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await Lock(db, $"{tenant:N}|closing|{request.SessionId:N}", ct);
        var hash = JsonSerializer.Serialize(new { request.SessionId, request.ExpectedVersion });
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == "session.closing.start" && x.IdempotencyKey == key.ToString(), ct);
        if (existing is not null) { if (existing.RequestHash != hash) return Problem(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST"); await tx.CommitAsync(ct); return Results.Content(existing.ResponsePayload!, "application/json", statusCode: existing.ResponseStatus ?? 200); }
        var session = await db.Set<TableSession>().FromSqlInterpolated($"select * from tables.table_session where id = {request.SessionId} and establishment_id = {tenant} and dining_table_id = {binding.DiningTableId} for update").SingleOrDefaultAsync(ct);
        if (session is null) return Results.NotFound();
        if (session.Version != request.ExpectedVersion) return await Conflict(db, tx, tenant, key, hash, "CONCURRENCY_CONFLICT");
        if (session.Status != "open") return await Conflict(db, tx, tenant, key, hash, "SESSION_INVALID_STATE");
        var pendingStatuses = new[] { "awaiting_preparation", "in_preparation", "paused", "ready", "awaiting_delivery_confirmation" };
        var pending = await (from item in db.Set<OrderItem>() join order in db.Set<Order>() on item.OrderId equals order.Id join production in db.Set<ProductionItem>() on item.Id equals production.OrderItemId where order.TableSessionId == session.Id && order.EstablishmentId == tenant && pendingStatuses.Contains(production.Status) select production.Id).AnyAsync(ct);
        if (pending) return await Conflict(db, tx, tenant, key, hash, "SESSION_HAS_PENDING_OPERATIONS");
        var now = DateTimeOffset.UtcNow; session.Status = "awaiting_payment"; session.ClosingStartedAt = now; session.UpdatedAt = now;
        AddEvent(db, tenant, "session-closing-started.v1", session, deviceId, now); AddAudit(db, tenant, "closing.start", session.Id, null, deviceId, http.HttpContext.TraceIdentifier, now, null);
        var payload = JsonSerializer.Serialize(Balance(session)); db.Add(new IdempotencyRecord { Id = Guid.NewGuid(), EstablishmentId = tenant, OperationType = "session.closing.start", IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = now }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static async Task<IResult> CancelClosing(Guid id, CancelClosingRequest request, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("user")) return Problem(403, "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id"); var permissions = await PermissionResolver.ResolveAsync(db, principal.RequiredGuid("sub"), DateTimeOffset.UtcNow, ct); if (!permissions.Contains("closing.cancel")) return Problem(403, "INSUFFICIENT_PERMISSION");
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Problem(400, "IDEMPOTENCY_KEY_REQUIRED");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, $"{tenant:N}|closing|{id:N}", ct); var hash = JsonSerializer.Serialize(new { id, request.ExpectedVersion, request.Reason });
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == "session.closing.cancel" && x.IdempotencyKey == key.ToString(), ct); if (existing is not null) { if (existing.RequestHash != hash) return Problem(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST"); await tx.CommitAsync(ct); return Results.Content(existing.ResponsePayload!, "application/json", statusCode: existing.ResponseStatus ?? 200); }
        var session = await db.Set<TableSession>().FromSqlInterpolated($"select * from tables.table_session where id = {id} and establishment_id = {tenant} for update").SingleOrDefaultAsync(ct); if (session is null) return Results.NotFound(); if (session.Version != request.ExpectedVersion) return Problem(409, "CONCURRENCY_CONFLICT"); if (session.Status is not ("closing" or "awaiting_payment")) return Problem(409, "SESSION_INVALID_STATE");
        var activePaymentStatuses = new[] { PaymentAttemptStatus.Created, PaymentAttemptStatus.AwaitingCustomerAction, PaymentAttemptStatus.Processing, PaymentAttemptStatus.Unknown };
        if (session.PaidAmount != 0 || session.ReservedAmount != 0 || await db.Set<PaymentAttempt>().AnyAsync(x => x.TableSessionId == id && activePaymentStatuses.Contains(x.Status), ct)) return Problem(409, "CLOSING_NOT_CANCELLABLE");
        session.Status = "open"; session.UpdatedAt = DateTimeOffset.UtcNow; AddEvent(db, tenant, "session-closing-cancelled.v1", session, principal.RequiredGuid("sub"), session.UpdatedAt); AddAudit(db, tenant, "closing.cancel", session.Id, principal.RequiredGuid("sub"), null, http.HttpContext.TraceIdentifier, session.UpdatedAt, request.Reason); var payload = JsonSerializer.Serialize(Balance(session)); db.Add(new IdempotencyRecord { Id = Guid.NewGuid(), EstablishmentId = tenant, OperationType = "session.closing.cancel", IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = session.UpdatedAt }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static async Task<IResult> DeviceBalance(ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) { if (!p.IsTokenType("device")) return Problem(403, "INVALID_TOKEN_TYPE"); var tenant = p.RequiredGuid("establishment_id"); var device = p.RequiredGuid("sub"); var currentDevice = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == device && x.EstablishmentId == tenant, ct); if (currentDevice is null) return Results.NotFound(); if (currentDevice.Status == "blocked") return Problem(403, "DEVICE_BLOCKED"); if (currentDevice.Status != "active" || currentDevice.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != p.FindFirstValue("credential_version")) return Problem(403, "DEVICE_CREDENTIAL_REVOKED"); var binding = await db.Set<DeviceTableBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == device && x.UnboundAt == null, ct); if (binding is null) return Results.NotFound(); var s = await db.Set<TableSession>().AsNoTracking().SingleOrDefaultAsync(x => x.DiningTableId == binding.DiningTableId && x.EstablishmentId == tenant && new[] { "open", "closing", "awaiting_payment", "partially_paid", "paid", "closed" }.Contains(x.Status), ct); return s is null ? Results.NotFound() : Results.Ok(Balance(s)); }
    private static async Task<IResult> OperationsBalance(Guid id, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) { if (!p.IsTokenType("user")) return Problem(403, "INVALID_TOKEN_TYPE"); var tenant = p.RequiredGuid("establishment_id"); var perms = await PermissionResolver.ResolveAsync(db, p.RequiredGuid("sub"), DateTimeOffset.UtcNow, ct); if (!perms.Contains("closing.view")) return Problem(403, "INSUFFICIENT_PERMISSION"); var s = await db.Set<TableSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.EstablishmentId == tenant, ct); return s is null ? Results.NotFound() : Results.Ok(Balance(s)); }
    private static object Balance(TableSession s) => new { sessionId = s.Id, status = s.Status, version = s.Version, subtotalAmount = s.SubtotalAmount, discountAmount = s.DiscountAmount, adjustmentAmount = s.AdjustmentAmount, totalAmount = s.TotalAmount, paidAmount = s.PaidAmount, reservedAmount = s.ReservedAmount, outstandingAmount = Math.Max(0m, s.TotalAmount - s.PaidAmount), availableToReserveAmount = Math.Max(0m, s.TotalAmount - s.PaidAmount - s.ReservedAmount), closingStartedAt = s.ClosingStartedAt, paidAt = s.PaidAt, closedAt = s.ClosedAt };
    private static void AddEvent(AppizzaDbContext db, Guid tenant, string type, TableSession s, Guid actor, DateTimeOffset now) { var id = Guid.NewGuid(); db.Add(new OutboxMessage { Id = id, EstablishmentId = tenant, EventType = type, SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventId = id, eventType = type, schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, aggregateId = s.Id, aggregateVersion = s.Version, actorId = actor }) }); }
    private static void AddAudit(AppizzaDbContext db, Guid tenant, string action, Guid aggregateId, Guid? user, Guid? device, string correlation, DateTimeOffset occurredAt, string? reason) => db.Add(new AuditEntry { Id = Guid.NewGuid(), EstablishmentId = tenant, Action = action, AggregateType = "table_session", AggregateId = aggregateId, ActorUserId = user, ActorDeviceId = device, CorrelationId = correlation, Reason = reason, OccurredAt = occurredAt });
    private static async Task Lock(AppizzaDbContext db, string key, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    private static async Task<IResult> Conflict(AppizzaDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Guid tenant, Guid key, string hash, string code) { var payload = JsonSerializer.Serialize(new { status = 409, errorCode = code }); db.Add(new IdempotencyRecord { Id = Guid.NewGuid(), EstablishmentId = tenant, OperationType = "session.closing.start", IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 409, ResponsePayload = payload, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Content(payload, "application/problem+json", statusCode: 409); }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}

public sealed record ClosingRequest(Guid SessionId, long ExpectedVersion);
public sealed record CancelClosingRequest(long ExpectedVersion, string Reason);
