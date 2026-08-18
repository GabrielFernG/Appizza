using System.Security.Claims;
using System.Text.Json;
using Appizza.Modules.Devices;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase5OrderStatusEndpoints
{
    private static readonly string[] ActiveSessionStatuses = ["open", "closing", "awaiting_payment", "partially_paid", "paid", "suspended"];

    public static IEndpointRouteBuilder MapPhase5OrderStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/table-device").RequireAuthorization();
        group.MapGet("/session/orders/status", SessionOrders);
        group.MapGet("/orders/{orderId:guid}", OrderDetail);
        return app;
    }

    private static async Task<IResult> SessionOrders(ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var context = await CurrentSession(principal, db, ct);
        if (context.Error is not null) return context.Error;
        var orders = await db.Set<Order>().AsNoTracking()
            .Where(x => x.EstablishmentId == context.Tenant && x.TableSessionId == context.SessionId)
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.OrderNumber).ToListAsync(ct);
        var projected = await Project(db, orders, includeSnapshot: false, ct);
        return Results.Ok(new { sessionId = context.SessionId, sessionVersion = context.SessionVersion, orders = projected });
    }

    private static async Task<IResult> OrderDetail(Guid orderId, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var context = await CurrentSession(principal, db, ct);
        if (context.Error is not null) return context.Error;
        var order = await db.Set<Order>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.EstablishmentId == context.Tenant && x.TableSessionId == context.SessionId, ct);
        if (order is null) return Results.NotFound();
        var projected = await Project(db, new List<Order> { order }, includeSnapshot: true, ct);
        return Results.Ok(projected[0]);
    }

    private static async Task<List<object>> Project(AppizzaDbContext db, List<Order> orders, bool includeSnapshot, CancellationToken ct)
    {
        if (orders.Count == 0) return [];
        var orderIds = orders.Select(x => x.Id).ToArray();
        var items = await db.Set<OrderItem>().AsNoTracking().Where(x => orderIds.Contains(x.OrderId)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var itemIds = items.Select(x => x.Id).ToArray();
        var production = await db.Set<ProductionItem>().AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId)).ToListAsync(ct);
        var productionIds = production.Select(x => x.Id).ToArray();
        var confirmations = await db.Set<DeliveryConfirmation>().AsNoTracking()
            .Where(x => productionIds.Contains(x.ProductionItemId) && (x.Status == "pending" || x.Status == "confirmed_manual" || x.Status == "confirmed_automatic" || x.Status == "contested"))
            .OrderByDescending(x => x.SequenceNumber).ToListAsync(ct);
        var contests = await db.Set<DeliveryContest>().AsNoTracking()
            .Where(x => productionIds.Contains(x.ProductionItemId) && x.Status == "open").ToListAsync(ct);
        var pendingRequests = await db.Set<OrderItemRequest>().AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId) && new[] { "pending_validation", "pending_customer_confirmation", "pending_operational_decision" }.Contains(x.Status)).ToListAsync(ct);
        var revisions = await db.Set<OrderItemRevision>().AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId)).ToListAsync(ct);
        var activeAttempts = await db.Set<ProductionAttempt>().AsNoTracking().Where(x => productionIds.Contains(x.ProductionItemId) && x.Status == "active").Select(x => new { x.ProductionItemId, x.AttemptNumber, x.StartedAt }).ToListAsync(ct);
        var openPauses = await db.Set<ProductionPause>().AsNoTracking().Where(x => productionIds.Contains(x.ProductionItemId) && x.ResumedAt == null).Select(x => new { x.ProductionItemId, x.PausedAt }).ToListAsync(ct);
        var openContests = await db.Set<DeliveryContest>().AsNoTracking().Where(x => productionIds.Contains(x.ProductionItemId) && x.Status == "open").Select(x => x.ProductionItemId).ToHashSetAsync(ct);
        var byItem = production.ToDictionary(x => x.OrderItemId);
        var currentConfirmations = confirmations.GroupBy(x => x.ProductionItemId).ToDictionary(x => x.Key, x => x.First());
        var currentOpenContests = contests.ToDictionary(x => x.ProductionItemId);
        var attempts = activeAttempts.ToDictionary(x => x.ProductionItemId);
        var pauses = openPauses.ToDictionary(x => x.ProductionItemId);
        var requests = pendingRequests.ToLookup(x => x.OrderItemId);
        var currentRevisions = revisions.ToDictionary(x => (x.OrderItemId, x.RevisionNumber));
        var result = new List<object>(orders.Count);
        foreach (var order in orders)
        {
            var publicItems = items.Where(x => x.OrderId == order.Id).Select(item =>
            {
                byItem.TryGetValue(item.Id, out var operational);
                var confirmation = operational is null ? null : currentConfirmations.GetValueOrDefault(operational.Id);
                var contest = operational is null ? null : currentOpenContests.GetValueOrDefault(operational.Id);
                var attempt = operational is null ? null : attempts.GetValueOrDefault(operational.Id);
                var pause = operational is null ? null : pauses.GetValueOrDefault(operational.Id);
                var hasAttempt = attempt is not null;
                var hasPause = pause is not null;
                var hasOpenContest = operational is not null && openContests.Contains(operational.Id);
                var status = PublicOrderStatusCalculator.Item(new(item.CommercialStatus, operational?.Status, operational?.RequiresProduction ?? false, hasAttempt, hasPause, requests[item.Id].Any(), hasOpenContest));
                var current = item.CurrentRevisionNumber > 0 ? currentRevisions.GetValueOrDefault((item.Id, item.CurrentRevisionNumber)) : null;
                object? snapshot = includeSnapshot ? ParseSnapshot(current?.Snapshot ?? item.Snapshot) : null;
                return new
                {
                    itemId = item.Id, item.ProductName, item.VariantName, item.ProductType, item.Quantity, item.UnitAmount, item.TotalAmount,
                    commercialStatus = item.CommercialStatus, publicStatus = status.Status, publicSubstatus = status.Substatus,
                    attentionReasons = status.AttentionReasons, version = operational?.Version ?? item.Version, item.CatalogRevisionId, item.CatalogVersion,
                    item.AvailabilityVersion, item.ConfigurationVersion, item.SnapshotSchemaVersion, item.CurrentRevisionNumber, snapshot,
                    delivery = confirmation is null && contest is null ? null : new
                    {
                        confirmationId = confirmation?.Id,
                        confirmationStatus = confirmation?.Status,
                        confirmationVersion = confirmation?.Version,
                        sequence = confirmation?.SequenceNumber,
                        requestedAt = confirmation?.RequestedAt,
                        expiresAt = confirmation?.ExpiresAt,
                        confirmedAt = confirmation?.ConfirmedAt,
                        contestId = contest?.Id,
                        contestStatus = contest?.Status,
                        contestVersion = contest?.Version,
                        contestReason = contest?.ResolutionNote,
                        contestedAt = confirmation?.ContestedAt,
                        attentionRequired = contest is not null
                    },
                    production = operational is null ? null : new { operational.RequiresProduction, operational.Status, operational.ReceivedAt, operational.AcceptedAt, operational.PreparationStartedAt, operational.ReadyAt, operational.CurrentAttemptNumber, operational.Version, activeAttemptNumber = hasAttempt ? attempt!.AttemptNumber : (int?)null, activeAttemptStartedAt = hasAttempt ? attempt!.StartedAt : (DateTimeOffset?)null, pausedAt = hasPause ? pause!.PausedAt : (DateTimeOffset?)null },
                    requests = includeSnapshot ? requests[item.Id].Select(x => new { x.Id, x.RequestType, x.Status, x.ReasonCode, x.RequestedAt, x.DecidedAt, x.Version }).ToArray() : null
                };
            }).ToArray();
            var aggregate = PublicOrderStatusCalculator.Order(publicItems.Select(x => new PublicOrderItemStatus(x.publicStatus, x.publicSubstatus, x.attentionReasons)));
            result.Add(new { orderId = order.Id, order.OrderNumber, order.SubmittedAt, commercialStatus = order.Status, publicStatus = aggregate.Status, publicSubstatus = aggregate.Substatus, attentionReasons = aggregate.AttentionReasons, order.SubtotalAmount, order.DiscountAmount, order.TotalAmount, order.Version, items = publicItems });
        }
        return result;
    }

    private static JsonElement ParseSnapshot(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static async Task<(Guid Tenant, Guid SessionId, long SessionVersion, IResult? Error)> CurrentSession(ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("device")) return (default, default, default, Error(403, "INVALID_TOKEN_TYPE", "Token de dispositivo necessário."));
        var deviceId = principal.RequiredGuid("sub"); var tenant = principal.RequiredGuid("establishment_id");
        var device = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId && x.EstablishmentId == tenant, ct);
        if (device is null) return (default, default, default, Results.NotFound());
        if (device.Status == "blocked") return (default, default, default, Error(403, "DEVICE_BLOCKED", "Dispositivo bloqueado."));
        if (device.Status != "active" || device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != principal.FindFirstValue("credential_version")) return (default, default, default, Error(403, "DEVICE_CREDENTIAL_REVOKED", "Credencial revogada."));
        var binding = await db.Set<DeviceTableBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.UnboundAt == null, ct);
        if (binding is null) return (default, default, default, Error(409, "DEVICE_NOT_BOUND", "Dispositivo sem vínculo ativo."));
        var session = await db.Set<TableSession>().AsNoTracking().SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.DiningTableId == binding.DiningTableId && ActiveSessionStatuses.Contains(x.Status), ct);
        if (session is null) return (default, default, default, Error(409, "TABLE_SESSION_NOT_ACTIVE", "Mesa sem sessão ativa."));
        return (tenant, session.Id, session.Version, null);
    }

    private static IResult Error(int status, string code, string detail) => Results.Problem(statusCode: status, title: detail, detail: detail, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}
