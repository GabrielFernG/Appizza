using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Appizza.Modules.Identity;
using Appizza.Modules.Auditing;
using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase7RefundEndpoints
{
    public static IEndpointRouteBuilder MapPhase7RefundEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/payments/{paymentId:guid}/refunds", Create).RequireAuthorization();
        app.MapPost("/api/v1/payments/refunds/{refundId:guid}/confirm-cash", ConfirmCash).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ConfirmCash(Guid refundId, ClaimsPrincipal principal, AppizzaDbContext db, RefundLifecycleService lifecycle, CancellationToken ct)
    {
        if (!principal.IsTokenType("user")) return Problem(403, "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id");
        var user = principal.RequiredGuid("sub");
        if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound();
        var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct);
        if (!permissions.Contains("payments.refund")) return Problem(403, "INSUFFICIENT_PERMISSION");
        try
        {
            var refund = await lifecycle.CompleteCashAsync(tenant, refundId, ct);
            return Results.Ok(new { refundId = refund.Id, paymentAttemptId = refund.PaymentAttemptId, amount = refund.Amount, reason = refund.Reason, status = "completed" });
        }
        catch (InvalidOperationException ex) when (ex.Message == "REFUND_NOT_FOUND") { return Results.NotFound(); }
        catch (InvalidOperationException ex) when (ex.Message is "REFUND_NOT_CASH" or "REFUND_INVALID_STATE") { return Problem(409, ex.Message); }
    }

    private sealed record CreateRefundRequest(decimal Amount, string? Reason);

    private static async Task<IResult> Create(Guid paymentId, CreateRefundRequest request, ClaimsPrincipal principal, HttpRequest http, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("user")) return Problem(403, "INSUFFICIENT_PERMISSION");
        var tenant = principal.RequiredGuid("establishment_id");
        var user = principal.RequiredGuid("sub");
        if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound();
        var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct);
        if (!permissions.Contains("payments.refund")) return Problem(403, "INSUFFICIENT_PERMISSION");
        if (request.Amount <= 0) return Problem(400, "REFUND_AMOUNT_INVALID");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) return Problem(400, "REFUND_REASON_REQUIRED");
        if (!Guid.TryParse(http.Headers["Idempotency-Key"].FirstOrDefault(), out var key)) return Problem(400, "IDEMPOTENCY_KEY_REQUIRED");
        var operation = "payments.refund.create";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant:N}|{paymentId:N}|{request.Amount:0.00}|{reason}")));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var attempt = await db.Set<PaymentAttempt>().FromSqlInterpolated($"select * from payments.payment_attempt where id = {paymentId} and establishment_id = {tenant} for update").SingleOrDefaultAsync(ct);
        if (attempt is null) return Results.NotFound();
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) return Problem(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
            await tx.CommitAsync(ct); return Results.Content(existing.ResponsePayload!, "application/json", statusCode: existing.ResponseStatus ?? 202);
        }
        if (attempt.Status != PaymentAttemptStatus.Approved) return Problem(409, "REFUND_PAYMENT_NOT_APPROVED");
        var completed = await db.Refunds.Where(x => x.PaymentAttemptId == paymentId && x.Status == RefundStatus.Completed).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var inFlight = await db.Refunds.Where(x => x.PaymentAttemptId == paymentId && (x.Status == RefundStatus.Created || x.Status == RefundStatus.Processing)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var available = Math.Max(0m, attempt.Amount - completed - inFlight);
        if (request.Amount > available) return Problem(409, "REFUND_AMOUNT_EXCEEDS_AVAILABLE");
        var now = DateTimeOffset.UtcNow; var refundId = Guid.NewGuid();
        var refund = new Refund { Id = refundId, EstablishmentId = tenant, PaymentAttemptId = paymentId, Amount = request.Amount, Reason = reason, Status = RefundStatus.Created, IdempotencyKey = key.ToString(), CreatedAt = now, UpdatedAt = now };
        db.Add(refund);
        if (attempt.Method != PaymentMethod.Cash)
            db.Add(new RefundProviderExecution { Id = Guid.NewGuid(), EstablishmentId = tenant, RefundId = refundId, Provider = attempt.Provider ?? "pending", ProviderIdempotencyKey = $"refund:{refundId:N}", Status = RefundProviderExecutionStatus.Pending, CreatedAt = now, UpdatedAt = now });
        var payload = JsonSerializer.Serialize(new { refundId, paymentAttemptId = paymentId, amount = request.Amount, reason, status = "created", completedAmount = completed, inFlightAmount = inFlight + request.Amount, availableAmount = available - request.Amount });
        db.Add(new AuditEntry { Id = Guid.NewGuid(), EstablishmentId = tenant, Action = "refund.created", AggregateType = "refund", AggregateId = refundId, OccurredAt = now });
        db.Add(new OutboxMessage { Id = Guid.NewGuid(), EstablishmentId = tenant, EventType = "refund-created.v1", SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventId = refundId, eventType = "refund-created.v1", schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, data = new { refundId, paymentAttemptId = paymentId, amount = request.Amount, status = "created" } }) });
        db.Add(new IdempotencyRecord { Id = Guid.NewGuid(), EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 202, ResponsePayload = payload, CreatedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Content(payload, "application/json", statusCode: 202);
    }

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code, detail: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}
