using System.Security.Claims;
using Appizza.Modules.Payments;
using Appizza.Persistence;
using Appizza.Payments.Application;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase7PaymentLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapPhase7PaymentLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/payments/attempts/{attemptId:guid}/confirm-cash", ConfirmCash).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ConfirmCash(Guid attemptId, ClaimsPrincipal principal, AppizzaDbContext db, PaymentAttemptLifecycleService lifecycle, CancellationToken ct)
    {
        if (!principal.IsTokenType("user")) return Problem(403, "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id");
        var user = principal.RequiredGuid("sub");
        if (!await db.Set<Appizza.Modules.Identity.User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound();
        var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct);
        if (!permissions.Contains("payments.confirm_cash")) return Problem(403, "INSUFFICIENT_PERMISSION");
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct);
        if (attempt is null) return Results.NotFound();
        if (attempt.Method != PaymentMethod.Cash) return Problem(409, "PAYMENT_METHOD_INVALID");
        if (attempt.Status is PaymentAttemptStatus.Declined or PaymentAttemptStatus.Cancelled or PaymentAttemptStatus.Expired) return Problem(409, "PAYMENT_ATTEMPT_INVALID_STATE");
        try
        {
            var settled = await lifecycle.SucceedAsync(tenant, attemptId, ct);
            return Results.Ok(new { attemptId = settled.Id, amount = settled.Amount, paymentMethod = "cash", status = settled.Status.ToString().ToLowerInvariant() });
        }
        catch (InvalidOperationException ex) when (ex.Message is "PAYMENT_ATTEMPT_NOT_FOUND") { return Results.NotFound(); }
        catch (InvalidOperationException ex) when (ex.Message is "PAYMENT_ATTEMPT_INVALID_STATE" or "PAYMENT_METHOD_INVALID") { return Problem(409, ex.Message); }
    }

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code, detail: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}
