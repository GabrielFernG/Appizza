using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Appizza.Modules.Devices;
using Appizza.Modules.Ordering;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase7PaymentPlanEndpoints
{
    public static IEndpointRouteBuilder MapPhase7PaymentPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/table-device/session/payment-plan", Create)
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Create(PaymentPlanRequest request, HttpRequest http, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("device")) return Problem(403, "INVALID_TOKEN_TYPE");
        if (!Guid.TryParse(http.Headers["Idempotency-Key"], out var key)) return Problem(400, "IDEMPOTENCY_KEY_REQUIRED");
        var tenant = principal.RequiredGuid("establishment_id");
        var deviceId = principal.RequiredGuid("sub");
        var device = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId && x.EstablishmentId == tenant, ct);
        if (device is null) return Results.NotFound();
        if (device.Status == "blocked") return Problem(403, "DEVICE_BLOCKED");
        if (device.Status != "active" || device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != principal.FindFirstValue("credential_version")) return Problem(403, "DEVICE_CREDENTIAL_REVOKED");
        var binding = await db.Set<DeviceTableBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.UnboundAt == null, ct);
        if (binding is null) return Results.NotFound();
        if (request.SessionId == Guid.Empty) return Problem(400, "INVALID_REQUEST");

        var canonical = Canonicalize(request);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({$"{tenant:N}|payment-plan|{request.SessionId:N}"}, 0))", ct);
        var operation = "payments.plan.create";
        var replay = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.OperationType == operation && x.IdempotencyKey == key.ToString(), ct);
        if (replay is not null)
        {
            if (replay.RequestHash != hash) return Problem(409, "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
            await tx.CommitAsync(ct);
            return Results.Content(replay.ResponsePayload ?? "{}", "application/json", statusCode: replay.ResponseStatus ?? 201);
        }

        var session = await db.Set<TableSession>().FromSqlInterpolated($"select * from tables.table_session where id = {request.SessionId} and establishment_id = {tenant} and dining_table_id = {binding.DiningTableId} for update").SingleOrDefaultAsync(ct);
        if (session is null) return Results.NotFound();
        if (session.Status is not ("open" or "awaiting_payment" or "partially_paid")) return Problem(409, "SESSION_INVALID_STATE");

        var allocations = await BuildAllocations(request, session, tenant, db, ct);
        if (allocations.Error is not null) return allocations.Error;
        var now = DateTimeOffset.UtcNow;
        var plan = new PaymentPlan { Id = Guid.NewGuid(), LogicalPlanId = Guid.NewGuid(), EstablishmentId = tenant, TableSessionId = session.Id, Version = 1, Mode = allocations.Mode!.Value, CreatedAt = now, UpdatedAt = now };
        foreach (var item in allocations.Items!) plan.Allocations.Add(new PaymentPlanAllocation { Id = Guid.NewGuid(), EstablishmentId = tenant, PaymentPlanId = plan.Id, OrderItemId = item.OrderItemId, StableOrder = item.StableOrder, Amount = item.Amount, CreatedAt = now });
        db.Add(plan);
        var payload = JsonSerializer.Serialize(new { planId = plan.Id, version = plan.Version, mode = request.Mode, tableSessionId = session.Id, allocations = plan.Allocations.Select(x => new { allocationId = x.Id, amount = x.Amount, stableOrder = x.StableOrder, participantId = (Guid?)null, orderItemId = x.OrderItemId }), authoritative = new { subtotalAmount = session.SubtotalAmount, discountAmount = session.DiscountAmount, totalAmount = session.TotalAmount, paidAmount = session.PaidAmount, reservedAmount = session.ReservedAmount, outstandingAmount = Math.Max(0m, session.TotalAmount - session.PaidAmount), availableToReserveAmount = Math.Max(0m, session.TotalAmount - session.PaidAmount - session.ReservedAmount) } });
        db.Add(new IdempotencyRecord { Id = Guid.NewGuid(), EstablishmentId = tenant, OperationType = operation, IdempotencyKey = key.ToString(), RequestHash = hash, ResponseStatus = 201, ResponsePayload = payload, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Content(payload, "application/json", statusCode: 201);
    }

    private static async Task<(PaymentPlanMode? Mode, List<AllocationDraft>? Items, IResult? Error)> BuildAllocations(PaymentPlanRequest request, TableSession session, Guid tenant, AppizzaDbContext db, CancellationToken ct)
    {
        var eligible = Math.Max(0m, session.TotalAmount - session.PaidAmount - session.ReservedAmount);
        switch (request.Mode?.Trim().ToLowerInvariant())
        {
            case "total": return (PaymentPlanMode.Total, [new(null, 0, eligible)], null);
            case "equal_split":
                if (request.PartCount is not > 0) return (null, null, Problem(400, "INVALID_REQUEST"));
                var parts = PaymentAllocationCalculator.EqualSplit(eligible, request.PartCount.Value);
                return (PaymentPlanMode.EqualSplit, parts.Select((amount, i) => new AllocationDraft(null, i, amount)).ToList(), null);
            case "custom_amount":
                if (request.Amount is not > 0 || decimal.Round(request.Amount.Value, 2) != request.Amount.Value || request.Amount.Value > eligible) return (null, null, Problem(422, "INVALID_PAYMENT_PLAN_ALLOCATION"));
                return (PaymentPlanMode.CustomAmount, [new(null, 0, request.Amount.Value)], null);
            case "by_item":
                if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.OrderItemId == Guid.Empty) || request.Items.Select(x => x.OrderItemId).Distinct().Count() != request.Items.Count) return (null, null, Problem(400, "INVALID_REQUEST"));
                var ids = request.Items.Select(x => x.OrderItemId).ToArray();
                var rows = await (from item in db.Set<OrderItem>() join order in db.Set<Order>() on item.OrderId equals order.Id where ids.Contains(item.Id) && order.EstablishmentId == tenant && order.TableSessionId == session.Id && item.CommercialStatus != "cancelled" select new { item.Id, item.TotalAmount }).ToListAsync(ct);
                if (rows.Count != ids.Length) return (null, null, Problem(404, "RESOURCE_NOT_FOUND"));
                var canonical = rows.OrderBy(x => x.Id).Select((x, i) => new AllocationDraft(x.Id, i, x.TotalAmount)).ToList();
                if (canonical.Sum(x => x.Amount) > eligible) return (null, null, Problem(422, "INVALID_PAYMENT_PLAN_ALLOCATION"));
                return (PaymentPlanMode.ByItem, canonical, null);
            default: return (null, null, Problem(400, "INVALID_REQUEST"));
        }
    }

    private static string Canonicalize(PaymentPlanRequest request)
    {
        var mode = request.Mode?.Trim().ToLowerInvariant();
        return mode switch
        {
            "total" => "{\"mode\":\"total\"}",
            "equal_split" => JsonSerializer.Serialize(new { mode, partCount = request.PartCount }),
            "custom_amount" => JsonSerializer.Serialize(new { mode, amount = decimal.Round(request.Amount ?? 0m, 2) }),
            "by_item" => JsonSerializer.Serialize(new { mode, items = (request.Items ?? []).Select(x => x.OrderItemId).OrderBy(x => x).ToArray() }),
            _ => JsonSerializer.Serialize(new { mode })
        };
    }

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
    private sealed record AllocationDraft(Guid? OrderItemId, int StableOrder, decimal Amount);
}

public sealed class PaymentPlanRequest
{
    public Guid SessionId { get; set; }
    public string? Mode { get; set; }
    public int? PartCount { get; set; }
    public decimal? Amount { get; set; }
    public List<PaymentPlanItemRequest>? Items { get; set; }
}

public sealed class PaymentPlanItemRequest { public Guid OrderItemId { get; set; } }
