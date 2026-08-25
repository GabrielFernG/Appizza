using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Appizza.Modules.Communications;
using Appizza.Modules.Devices;
using Appizza.Modules.Identity;
using Appizza.Modules.Media;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase6CommunicationEndpoints
{
    public static IEndpointRouteBuilder MapPhase6CommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        var operations = app.MapGroup("/api/v1/operations/communications").RequireAuthorization();
        operations.MapGet("", List);
        operations.MapPost("", Create);
        operations.MapPost("/{id:guid}/publish", Publish);
        operations.MapPost("/{id:guid}/pause", Pause);
        operations.MapPost("/{id:guid}/archive", Archive);
        app.MapGet("/api/v1/table-device/session/communications", Current).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var denied = await Authorize(principal, db, "communications.view", ct); if (denied is not null) return denied;
        var tenant = principal.RequiredGuid("establishment_id");
        return Results.Ok(await db.Communications.AsNoTracking().Where(x => x.EstablishmentId == tenant).OrderByDescending(x => x.Priority).ThenBy(x => x.Id).ToListAsync(ct));
    }

    private static async Task<IResult> Create(CreateCommunicationRequest request, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var denied = await Authorize(principal, db, "communications.create", ct); if (denied is not null) return denied;
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out _)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED");
        if (request.MediaType is not ("image" or "video") || request.StartsAt >= request.EndsAt || string.IsNullOrWhiteSpace(request.Title)) return Error(400, "COMMUNICATION_INVALID");
        var tenant = principal.RequiredGuid("establishment_id");
        if (request.MediaAssetId is not null && !await db.Set<MediaAsset>().AnyAsync(x => x.Id == request.MediaAssetId && x.EstablishmentId == tenant && x.Status == "ready", ct)) return Results.NotFound();
        var now = DateTimeOffset.UtcNow;
        var entity = new Communication { Id = Guid.NewGuid(), EstablishmentId = tenant, Title = request.Title.Trim(), Body = request.Body, MediaAssetId = request.MediaAssetId, MediaType = request.MediaType, Priority = request.Priority, StartsAt = request.StartsAt, EndsAt = request.EndsAt, CreatedAt = now, UpdatedAt = now };
        db.Add(entity); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/operations/communications/{entity.Id}", entity);
    }

    private static Task<IResult> Publish(Guid id, TransitionRequest? request, HttpRequest http, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) => Transition(id, request, http, CommunicationStatuses.Published, "communications.publish", "communication-published.v1", p, db, ct);
    private static Task<IResult> Pause(Guid id, TransitionRequest? request, HttpRequest http, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) => Transition(id, request, http, CommunicationStatuses.Paused, "communications.edit", "communication-paused.v1", p, db, ct);
    private static Task<IResult> Archive(Guid id, TransitionRequest? request, HttpRequest http, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) => Transition(id, request, http, CommunicationStatuses.Archived, "communications.edit", "communication-archived.v1", p, db, ct);

    private static async Task<IResult> Transition(Guid id, TransitionRequest? request, HttpRequest http, string status, string permission, string eventType, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var denied = await Authorize(principal, db, permission, ct); if (denied is not null) return denied;
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Error(400, "IDEMPOTENCY_KEY_REQUIRED");
        var tenant = principal.RequiredGuid("establishment_id"); var operation = $"communications.{status}.{id:N}"; var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, status, expectedVersion = request?.ExpectedVersion }))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N") + "|" + operation + "|" + key.ToString("N")}, 0))", ct);
        var replay = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (replay is not null) { if (replay.RequestHash != hash) return Error(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST"); await tx.CommitAsync(ct); return Results.Content(replay.ResponsePayload ?? "{}", "application/json", statusCode: replay.ResponseStatus ?? 200); }
        var entity = await db.Communications.SingleOrDefaultAsync(x => x.Id == id && x.EstablishmentId == tenant, ct); if (entity is null) return Results.NotFound();
        if (request?.ExpectedVersion is not null && request.ExpectedVersion != entity.Version) return Error(409, "CONCURRENCY_CONFLICT");
        var valid = (entity.Status, status) switch { (CommunicationStatuses.Draft, CommunicationStatuses.Published) => true, (CommunicationStatuses.Published, CommunicationStatuses.Paused or CommunicationStatuses.Archived) => true, (CommunicationStatuses.Paused, CommunicationStatuses.Published or CommunicationStatuses.Archived) => true, _ => false };
        if (!valid) return Error(409, "COMMUNICATION_INVALID_TRANSITION");
        entity.Status = status; entity.UpdatedAt = DateTimeOffset.UtcNow;
        db.Add(new OutboxMessage { Id = Guid.NewGuid(), EstablishmentId = tenant, EventType = eventType, SchemaVersion = 1, OccurredAt = DateTimeOffset.UtcNow, Payload = JsonSerializer.Serialize(new { communicationId = id, establishmentId = tenant, status }) });
        await db.SaveChangesAsync(ct); var payload = JsonSerializer.Serialize(entity); db.IdempotencyRecords.Add(new IdempotencyRecord { Id = Guid.NewGuid(), EstablishmentId = tenant, IdempotencyKey = key.ToString(), OperationType = operation, RequestHash = hash, ResponseStatus = 200, ResponsePayload = payload, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json", statusCode: 200);
    }

    private static async Task<IResult> Current(ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("device")) return Error(403, "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id");
        var device = await db.Set<Device>().SingleOrDefaultAsync(x => x.Id == principal.RequiredGuid("sub") && x.EstablishmentId == tenant && x.Status == "active", ct); if (device is null) return Results.NotFound();
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Communications.AsNoTracking().Where(x => x.EstablishmentId == tenant && x.Status == CommunicationStatuses.Published && x.StartsAt <= now && x.EndsAt > now).OrderByDescending(x => x.Priority).ThenBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Body, x.MediaAssetId, x.MediaType, x.StartsAt, x.EndsAt, x.Priority, x.Version }).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult?> Authorize(ClaimsPrincipal principal, AppizzaDbContext db, string permission, CancellationToken ct)
    { if (!principal.IsTokenType("user")) return Error(403, "INVALID_TOKEN_TYPE"); var user = principal.RequiredGuid("sub"); var tenant = principal.RequiredGuid("establishment_id"); if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound(); var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct); return permissions.Contains(permission) ? null : Error(403, "INSUFFICIENT_PERMISSION"); }
    private static IResult Error(int status, string code) => Results.Problem(statusCode: status, title: code, detail: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
    private sealed record CreateCommunicationRequest(string Title, string? Body, Guid? MediaAssetId, string MediaType, int Priority, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
    private sealed record TransitionRequest(long? ExpectedVersion);
}
