using System.Security.Claims;
using Appizza.Modules.Devices;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Appizza.Payments.Application;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase7PaymentAttemptEndpoints
{
    public static IEndpointRouteBuilder MapPhase7PaymentAttemptEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/table-device/payments/attempts", Create).RequireAuthorization();
        app.MapPost("/api/v1/table-device/payments/attempts/{attemptId:guid}/process", Process).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Process(Guid attemptId, ClaimsPrincipal principal, AppizzaDbContext db, PaymentProcessingService processing, CancellationToken ct)
    {
        if (!principal.IsTokenType("device")) return Problem(403, "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id");
        var deviceId = principal.RequiredGuid("sub");
        var device = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId && x.EstablishmentId == tenant, ct);
        if (device is null) return Results.NotFound();
        if (device.Status == "blocked") return Problem(403, "DEVICE_BLOCKED");
        if (device.Status != "active" || device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != principal.FindFirstValue("credential_version")) return Problem(403, "DEVICE_CREDENTIAL_REVOKED");
        var binding = await db.Set<DeviceTableBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.UnboundAt == null, ct);
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct);
        if (attempt is null) return Results.NotFound();
        var session = await db.Set<TableSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == attempt.TableSessionId && x.EstablishmentId == tenant, ct);
        if (binding is null || session is null || binding.DiningTableId != session.DiningTableId) return Results.NotFound();
        if (attempt.Method == PaymentMethod.Cash) return Problem(409, "CASH_DOES_NOT_USE_PROVIDER");
        if (attempt.Status is PaymentAttemptStatus.Approved or PaymentAttemptStatus.Declined or PaymentAttemptStatus.Cancelled or PaymentAttemptStatus.Expired or PaymentAttemptStatus.Unknown) return Problem(409, "PAYMENT_ATTEMPT_INVALID_STATE");
        try
        {
            var result = await processing.StartAsync(tenant, attemptId, ct);
            await processing.ApplyResultAsync(tenant, attemptId, result, ct);
            return Results.Ok(new { attemptId, status = result.Status.Trim().ToLowerInvariant() });
        }
        catch (InvalidOperationException ex) when (ex.Message is "PAYMENT_ATTEMPT_NOT_FOUND") { return Results.NotFound(); }
        catch (InvalidOperationException ex) when (ex.Message is "CASH_DOES_NOT_USE_PROVIDER" or "PAYMENT_ATTEMPT_INVALID_STATE") { return Problem(409, ex.Message); }
    }

    private static async Task<IResult> Create(PaymentAttemptRequest request, HttpRequest http, ClaimsPrincipal principal, PaymentAttemptReservationService service, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("device")) return Results.Problem(statusCode: 403, title: "INVALID_TOKEN_TYPE");
        if (request.PaymentPlanId == Guid.Empty || request.AllocationIds is null || request.PaymentMethod is null || !Enum.TryParse<PaymentMethod>(request.PaymentMethod, true, out var method)) return Results.Problem(statusCode: 400, title: "INVALID_REQUEST");
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Results.Problem(statusCode: 400, title: "IDEMPOTENCY_KEY_REQUIRED");
        var deviceId = principal.RequiredGuid("sub");
        var tenant = principal.RequiredGuid("establishment_id");
        var device = await db.Set<Device>().SingleOrDefaultAsync(x => x.Id == deviceId && x.EstablishmentId == tenant, ct);
        if (device is null) return Results.NotFound();
        if (device.Status == "blocked") return Results.Problem(statusCode: 403, title: "DEVICE_BLOCKED");
        if (device.Status != "active" || device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != principal.FindFirstValue("credential_version")) return Results.Problem(statusCode: 403, title: "DEVICE_CREDENTIAL_REVOKED");
        var binding = await db.Set<DeviceTableBinding>().SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.UnboundAt == null, ct);
        var session = await db.Set<TableSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TableSessionId && x.EstablishmentId == tenant, ct);
        if (binding is null || session is null || binding.DiningTableId != session.DiningTableId) return Results.NotFound();
        try
        {
            var result = await service.CreateAsync(new PaymentAttemptReservationCommand(tenant, request.TableSessionId, request.PaymentPlanId, request.AllocationIds, method, key.ToString()), ct);
            return Results.Json(new { attemptId = result.Attempt.Id, paymentPlanId = result.Attempt.PaymentPlanId, amount = result.Attempt.Amount, paymentMethod = method.ToString().ToLowerInvariant(), status = result.Attempt.Status.ToString() }, statusCode: result.Replay ? 200 : 201);
        }
        catch (InvalidOperationException ex) when (ex.Message is "RESOURCE_NOT_FOUND") { return Results.NotFound(); }
        catch (InvalidOperationException ex) when (ex.Message is "INSUFFICIENT_AVAILABLE_BALANCE" or "ALLOCATION_UNAVAILABLE") { return Problem(409, ex.Message); }
        catch (InvalidOperationException ex) when (ex.Message is "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST" or "SESSION_INVALID_STATE") { return Problem(409, ex.Message); }
        catch (InvalidOperationException) { return Problem(400, "INVALID_REQUEST"); }
    }

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code, detail: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}

public sealed class PaymentAttemptRequest
{
    public Guid TableSessionId { get; set; }
    public Guid PaymentPlanId { get; set; }
    public List<Guid>? AllocationIds { get; set; }
    public string? PaymentMethod { get; set; }
}
