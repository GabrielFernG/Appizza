using System.Security.Claims;
using System.Text.Json;
using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public sealed record ProductionActionRequest(long? ExpectedVersion = null, string? ReasonCode = null, string? Description = null);

public static class Phase5ProductionEndpoints
{
    public static IEndpointRouteBuilder MapPhase5ProductionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/operations/kitchen/production-items").RequireAuthorization();
        group.MapPost("/{id:guid}/start-preparation", (Guid id, ProductionActionRequest? body, HttpRequest http, ClaimsPrincipal user, AppizzaDbContext db, CancellationToken ct) => Execute(id, body, http, user, db, "kitchen.production.start", "kitchen.start-preparation", "start", ct));
        group.MapPost("/{id:guid}/pause", (Guid id, ProductionActionRequest? body, HttpRequest http, ClaimsPrincipal user, AppizzaDbContext db, CancellationToken ct) => Execute(id, body, http, user, db, "kitchen.production.pause", "kitchen.pause", "pause", ct));
        group.MapPost("/{id:guid}/resume", (Guid id, ProductionActionRequest? body, HttpRequest http, ClaimsPrincipal user, AppizzaDbContext db, CancellationToken ct) => Execute(id, body, http, user, db, "kitchen.production.resume", "kitchen.resume", "resume", ct));
        group.MapPost("/{id:guid}/fail-attempt", (Guid id, ProductionActionRequest? body, HttpRequest http, ClaimsPrincipal user, AppizzaDbContext db, CancellationToken ct) => Execute(id, body, http, user, db, "kitchen.production.fail", "kitchen.fail-attempt", "fail", ct));
        group.MapPost("/{id:guid}/restart", (Guid id, ProductionActionRequest? body, HttpRequest http, ClaimsPrincipal user, AppizzaDbContext db, CancellationToken ct) => Execute(id, body, http, user, db, "kitchen.production.restart", "kitchen.restart", "restart", ct));
        group.MapPost("/{id:guid}/ready", (Guid id, ProductionActionRequest? body, HttpRequest http, ClaimsPrincipal user, AppizzaDbContext db, CancellationToken ct) => Execute(id, body, http, user, db, "kitchen.production.ready", "kitchen.ready", "ready", ct));
        return app;
    }

    private static async Task<IResult> Execute(Guid id, ProductionActionRequest? request, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, string permission, string operation, string action, CancellationToken ct)
    {
        var denied = await Authorize(principal, db, permission, ct); if (denied is not null) return denied;
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key UUID é obrigatório.");
        request ??= new();
        if ((action is "pause" or "fail") && string.IsNullOrWhiteSpace(request.ReasonCode)) return Error(400, "REASON_REQUIRED", "Motivo é obrigatório.");
        var tenant = principal.RequiredGuid("establishment_id"); var user = principal.RequiredGuid("sub");
        var hash = Phase4Pricing.Hash(JsonSerializer.Serialize(new { id, request.ExpectedVersion, request.ReasonCode, request.Description }));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N") + "|" + operation + "|" + key.ToString("N")}, 0))", ct);
        var replay = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (replay is not null)
        {
            if (replay.RequestHash != hash) return Error(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", "A chave foi usada com outro request.");
            await tx.CommitAsync(ct); return Results.Content(replay.ResponsePayload!, replay.ResponseStatus == 409 ? "application/problem+json" : "application/json", statusCode: replay.ResponseStatus);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE id = {id} AND establishment_id = {tenant} FOR UPDATE", ct);
        db.ChangeTracker.Clear();
        denied = await Authorize(principal, db, permission, ct); if (denied is not null) { await tx.RollbackAsync(ct); return denied; }
        var item = await db.Set<ProductionItem>().SingleOrDefaultAsync(x => x.Id == id && x.EstablishmentId == tenant, ct);
        if (item is null) return Results.NotFound();
        if (request.ExpectedVersion is long expected && expected != item.Version) return await Conflict(db, tx, tenant, key, operation, hash, "CONCURRENCY_CONFLICT", "Versão desatualizada.", ct);
        var now = DateTimeOffset.UtcNow;
        var outcome = await Apply(action, item, request, user, now, db, ct);
        if (!outcome.Success) return await Conflict(db, tx, tenant, key, operation, hash, outcome.Code!, outcome.Detail!, ct);
        item.UpdatedAt = now;
        db.Add(new ProductionStatusHistory { Id = Guid.NewGuid(), ProductionItemId = item.Id, PreviousStatus = outcome.PreviousStatus, NewStatus = item.Status, UserId = user, ChangedAt = now });
        var eventId = Guid.NewGuid();
        db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = outcome.EventType!, SchemaVersion = 1, Payload = JsonSerializer.Serialize(new { eventId, eventType = outcome.EventName, schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, data = new { productionItemId = item.Id, previousStatus = outcome.PreviousStatus, resultingStatus = item.Status, attemptNumber = item.CurrentAttemptNumber } }), OccurredAt = now });
        await db.SaveChangesAsync(ct);
        var payload = JsonSerializer.Serialize(new { id = item.Id, status = item.Status, version = item.Version, currentAttempt = item.CurrentAttemptNumber, preparationStartedAt = item.PreparationStartedAt, readyAt = item.ReadyAt, updatedAt = item.UpdatedAt });
        db.Add(new IdempotencyRecord { EstablishmentId = tenant, IdempotencyKey = key.ToString(), OperationType = operation, RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json");
    }

    private static async Task<Outcome> Apply(string action, ProductionItem item, ProductionActionRequest request, Guid user, DateTimeOffset now, AppizzaDbContext db, CancellationToken ct)
    {
        var previous = item.Status;
        var activeAttempt = await db.Set<ProductionAttempt>().SingleOrDefaultAsync(x => x.ProductionItemId == item.Id && x.Status == "active", ct);
        var openPause = await db.Set<ProductionPause>().SingleOrDefaultAsync(x => x.ProductionItemId == item.Id && x.ResumedAt == null, ct);
        switch (action)
        {
            case "start" when ProductionLifecycle.CanStart(item.Status, activeAttempt is not null):
                item.Status = "in_preparation"; item.PreparationStartedAt ??= now; item.CurrentAttemptNumber = 1;
                db.Add(new ProductionAttempt { Id = Guid.NewGuid(), ProductionItemId = item.Id, AttemptNumber = 1, StartedAt = now, CreatedAt = now, CreatedByUserId = user });
                return Outcome.Ok(previous, "production-item-preparation-started.v1", "ProductionItemPreparationStarted");
            case "pause" when ProductionLifecycle.CanPause(item.Status, activeAttempt is not null, openPause is not null):
                item.Status = "paused"; db.Add(new ProductionPause { Id = Guid.NewGuid(), ProductionItemId = item.Id, ProductionAttemptId = activeAttempt!.Id, ReasonCode = request.ReasonCode!, Description = request.Description, PausedAt = now, PausedByUserId = user });
                return Outcome.Ok(previous, "production-item-paused.v1", "ProductionItemPaused");
            case "resume" when ProductionLifecycle.CanResume(item.Status, activeAttempt is not null, openPause is not null):
                openPause!.ResumedAt = now; openPause.ResumedByUserId = user; item.Status = "in_preparation";
                return Outcome.Ok(previous, "production-item-resumed.v1", "ProductionItemResumed");
            case "fail" when ProductionLifecycle.CanFail(item.Status, activeAttempt is not null):
                activeAttempt!.Status = "failed"; activeAttempt.FinishedAt = now; activeAttempt.FailureReasonCode = request.ReasonCode; activeAttempt.FailureDescription = request.Description; item.Status = "paused";
                return Outcome.Ok(previous, "production-attempt-failed.v1", "ProductionAttemptFailed");
            case "restart":
                var last = await db.Set<ProductionAttempt>().Where(x => x.ProductionItemId == item.Id).OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(ct);
                if (!ProductionLifecycle.CanRestart(item.Status, activeAttempt is not null, openPause is not null, last?.Status)) break;
                item.CurrentAttemptNumber = last!.AttemptNumber + 1; item.Status = "in_preparation";
                db.Add(new ProductionAttempt { Id = Guid.NewGuid(), ProductionItemId = item.Id, AttemptNumber = item.CurrentAttemptNumber, StartedAt = now, CreatedAt = now, CreatedByUserId = user });
                return Outcome.Ok(previous, "production-attempt-restarted.v1", "ProductionAttemptRestarted");
            case "ready" when ProductionLifecycle.CanReady(item.Status, activeAttempt is not null, openPause is not null):
                activeAttempt!.Status = "completed"; activeAttempt.FinishedAt = now; item.Status = "ready"; item.ReadyAt = now;
                return Outcome.Ok(previous, "production-item-ready.v1", "ProductionItemReady");
        }
        var code = action switch { "start" => "PRODUCTION_ITEM_ALREADY_STARTED", "pause" => "PRODUCTION_ITEM_ALREADY_PAUSED", "resume" => "PRODUCTION_ITEM_NOT_PAUSED", "fail" => "PRODUCTION_ATTEMPT_ALREADY_FINISHED", "restart" => "PRODUCTION_ATTEMPT_RESTART_NOT_ALLOWED", "ready" => "PRODUCTION_ITEM_ALREADY_READY", _ => "PRODUCTION_ITEM_INVALID_STATE" };
        return Outcome.Fail(code, "Ação incompatível com o estado operacional atual.");
    }

    private static async Task<IResult> Conflict(AppizzaDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Guid tenant, Guid key, string operation, string hash, string code, string detail, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { type = "about:blank", title = detail, status = 409, detail, errorCode = code });
        db.Add(new IdempotencyRecord { EstablishmentId = tenant, IdempotencyKey = key.ToString(), OperationType = operation, RequestHash = hash, ResponseStatus = 409, ResponsePayload = payload, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/problem+json", statusCode: 409);
    }
    private static async Task<IResult?> Authorize(ClaimsPrincipal p, AppizzaDbContext db, string permission, CancellationToken ct) { if (!p.IsTokenType("user")) return Error(403, "INVALID_TOKEN_TYPE", "Token de funcionário necessário."); var user = p.RequiredGuid("sub"); var tenant = p.RequiredGuid("establishment_id"); if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound(); var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct); return permissions.Contains(permission) ? null : Error(403, "INSUFFICIENT_PERMISSION", "Permissão insuficiente."); }
    private static IResult Error(int status, string code, string detail) => Results.Problem(statusCode: status, title: detail, detail: detail, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
    private sealed record Outcome(bool Success, string? PreviousStatus, string? EventType, string? EventName, string? Code, string? Detail) { public static Outcome Ok(string previous, string eventType, string eventName) => new(true, previous, eventType, eventName, null, null); public static Outcome Fail(string code, string detail) => new(false, null, null, null, code, detail); }
}
