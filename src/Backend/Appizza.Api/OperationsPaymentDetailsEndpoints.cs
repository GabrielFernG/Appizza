using System.Security.Claims;
using Appizza.Modules.Identity;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class OperationsPaymentDetailsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsPaymentDetailsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/operations/sessions/{sessionId:guid}/payments", Get).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Get(Guid sessionId, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("user")) return Results.Problem(statusCode: 403, title: "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id");
        var user = principal.RequiredGuid("sub");
        if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound();
        var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct);
        if (!permissions.Contains("payments.view")) return Results.Problem(statusCode: 403, title: "INSUFFICIENT_PERMISSION");
        var sessionExists = await db.Set<TableSession>().AsNoTracking().AnyAsync(x => x.Id == sessionId && x.EstablishmentId == tenant, ct);
        if (!sessionExists) return Results.NotFound();
        var plans = await db.Set<PaymentPlan>().AsNoTracking().Where(x => x.TableSessionId == sessionId && x.EstablishmentId == tenant).OrderByDescending(x => x.Version).Select(x => new { id = x.Id, logicalPlanId = x.LogicalPlanId, sessionId = x.TableSessionId, version = x.Version, mode = x.Mode.ToString(), createdAt = x.CreatedAt, updatedAt = x.UpdatedAt, allocations = x.Allocations.OrderBy(a => a.StableOrder).Select(a => new { id = a.Id, amount = a.Amount, stableOrder = a.StableOrder, participantId = a.ParticipantId, orderItemId = a.OrderItemId }) }).ToListAsync(ct);
        var attempts = await db.Set<PaymentAttempt>().AsNoTracking().Where(x => x.TableSessionId == sessionId && x.EstablishmentId == tenant).OrderBy(x => x.CreatedAt).Select(x => new { id = x.Id, paymentPlanId = x.PaymentPlanId, paymentPlanVersion = x.PaymentPlanVersion, method = x.Method.ToString(), status = x.Status.ToString(), amount = x.Amount, reservedAmount = x.ReservedAmount, provider = x.Provider, providerReference = x.ProviderReference, version = x.Version, createdAt = x.CreatedAt, updatedAt = x.UpdatedAt, approvedAt = x.ApprovedAt }).ToListAsync(ct);
        var attemptIds = attempts.Select(x => x.id).ToArray();
        var refunds = await db.Refunds.AsNoTracking().Where(x => attemptIds.Contains(x.PaymentAttemptId) && x.EstablishmentId == tenant).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var refundIds = refunds.Select(x => x.Id).ToArray();
        var executions = await db.RefundProviderExecutions.AsNoTracking().Where(x => refundIds.Contains(x.RefundId) && x.EstablishmentId == tenant).ToListAsync(ct);
        var paymentAttempts = attempts.Select(attempt =>
        {
            var rows = refunds.Where(x => x.PaymentAttemptId == attempt.id).ToArray();
            var completed = attempt.status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? rows.Where(x => x.Status == RefundStatus.Completed).Sum(x => x.Amount) : 0m;
            var inFlight = attempt.status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? rows.Where(x => x.Status is RefundStatus.Created or RefundStatus.Processing).Sum(x => x.Amount) : 0m;
            var available = Math.Max(0m, attempt.amount - completed - inFlight);
            return new
            {
                attempt.id, attempt.paymentPlanId, attempt.paymentPlanVersion, attempt.method, attempt.status, attempt.amount, attempt.reservedAmount, attempt.provider, attempt.providerReference, attempt.version, attempt.createdAt, attempt.updatedAt, attempt.approvedAt,
                refundSummary = new { completedAmount = completed, inFlightAmount = inFlight, availableAmount = available },
                refunds = rows.Select(refund => new { refundId = refund.Id, amount = refund.Amount, reason = refund.Reason, status = refund.Status.ToString(), createdAt = refund.CreatedAt, updatedAt = refund.UpdatedAt, reconciliationRequired = executions.Where(e => e.RefundId == refund.Id).Any(e => e.ReconciliationRequired) }).ToArray()
            };
        }).ToArray();
        return Results.Ok(new { sessionId, paymentPlans = plans, paymentAttempts });
    }
}
