namespace Appizza.Modules.Kitchen;

public static class PublicOrderStatuses
{
    public const string Received = "received";
    public const string Preparing = "preparing";
    public const string Ready = "ready";
    public const string OnTheWay = "on_the_way";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
}

public sealed record PublicOrderItemState(
    string CommercialStatus,
    string? ProductionStatus,
    bool RequiresProduction = true,
    bool HasActiveAttempt = false,
    bool HasOpenPause = false,
    bool HasPendingRequest = false,
    bool HasOpenDeliveryContest = false,
    bool HasUncompensatedProductionRejection = false,
    bool PauseRequiresAction = false);

public sealed record PublicOrderItemStatus(string Status, string Substatus, IReadOnlyList<string> AttentionReasons);
public sealed record PublicOrderStatus(string Status, string? Substatus, IReadOnlyList<string> AttentionReasons);

public static class PublicOrderStatusCalculator
{
    private static readonly Dictionary<string, int> StageRank = new(StringComparer.Ordinal)
    {
        [PublicOrderStatuses.Received] = 0,
        [PublicOrderStatuses.Preparing] = 1,
        [PublicOrderStatuses.Ready] = 2,
        [PublicOrderStatuses.OnTheWay] = 3,
        [PublicOrderStatuses.Delivered] = 4
    };

    public static PublicOrderItemStatus Item(PublicOrderItemState state)
    {
        var status = state.CommercialStatus == "cancelled"
            ? PublicOrderStatuses.Cancelled
            : state.ProductionStatus switch
            {
                "delivered" => PublicOrderStatuses.Delivered,
                "awaiting_delivery_confirmation" => PublicOrderStatuses.OnTheWay,
                "ready" => PublicOrderStatuses.Ready,
                "in_preparation" or "paused" => PublicOrderStatuses.Preparing,
                _ => PublicOrderStatuses.Received
            };

        var reasons = AttentionReasons(state);
        var substatus = reasons.Count > 0 ? "attention_required" : state.CommercialStatus == "cancelled" ? "cancelled" : state.ProductionStatus switch
        {
            null => "pending_kitchen_intake",
            "awaiting_acceptance" => "awaiting_kitchen_acceptance",
            "accepted" or "awaiting_preparation" => "awaiting_preparation",
            "in_preparation" => state.HasActiveAttempt ? "preparing" : "awaiting_preparation",
            "paused" => "paused",
            "ready" => "ready",
            "awaiting_delivery_confirmation" => "awaiting_delivery_confirmation",
            "delivered" => "delivered",
            _ => "attention_required"
        };

        return new(status, substatus, reasons);
    }

    public static PublicOrderStatus Order(IEnumerable<PublicOrderItemStatus> source)
    {
        var items = source.ToArray();
        if (items.Length == 0) return new(PublicOrderStatuses.Received, "pending_kitchen_intake", []);
        var reasons = items.SelectMany(x => x.AttentionReasons).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (items.All(x => x.Status == PublicOrderStatuses.Cancelled)) return new(PublicOrderStatuses.Cancelled, reasons.Length > 0 ? "attention_required" : null, reasons);
        var active = items.Where(x => x.Status != PublicOrderStatuses.Cancelled).ToArray();
        var status = active.All(x => x.Status == PublicOrderStatuses.Delivered)
            ? PublicOrderStatuses.Delivered
            : active.OrderBy(x => StageRank[x.Status]).First().Status;
        var partiallyCancelled = active.Length != items.Length;
        var substatus = reasons.Length > 0 ? "attention_required" : partiallyCancelled ? "partially_cancelled" : null;
        return new(status, substatus, reasons);
    }

    private static List<string> AttentionReasons(PublicOrderItemState state)
    {
        var reasons = new List<string>(4);
        if (state.HasPendingRequest) reasons.Add("order_item_request_pending");
        if (state.HasOpenDeliveryContest) reasons.Add("delivery_contest_open");
        if (state.HasUncompensatedProductionRejection) reasons.Add("production_rejection_pending_commercial_consequence");
        if (state.PauseRequiresAction) reasons.Add("production_pause_requires_action");
        return reasons;
    }
}
