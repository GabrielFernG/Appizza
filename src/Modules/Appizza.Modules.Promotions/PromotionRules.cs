namespace Appizza.Modules.Promotions;

public sealed record PromotionCandidate(Guid PromotionId, Guid VersionId, string Name, string Kind, string Scope, decimal Value, int Priority, IReadOnlySet<Guid> ProductIds);
public sealed record PromotionItem(Guid ProductId, decimal TotalAmount);
public static class PromotionRules
{
    public static PromotionDiscount? Select(IEnumerable<PromotionCandidate> promotions, IEnumerable<PromotionItem> items)
    {
        var source = items.ToArray(); var candidates = promotions.Select(p => { var eligible = p.Scope == PromotionScopes.EntireOrder ? source : source.Where(i => p.ProductIds.Contains(i.ProductId)).ToArray(); var baseAmount = Round(eligible.Sum(i => i.TotalAmount)); if (baseAmount <= 0) return null; var discount = p.Kind == PromotionKinds.Percentage ? Round(baseAmount * p.Value / 100m) : Math.Min(Round(p.Value), baseAmount); return discount <= 0 ? null : new PromotionDiscount(p.PromotionId, p.VersionId, p.Name, baseAmount, Math.Min(discount, baseAmount), p.Kind, p.Scope, p.Priority, p.Value, System.Text.Json.JsonSerializer.Serialize(p.ProductIds.OrderBy(x => x)));
        }).Where(x => x is not null).Cast<PromotionDiscount>(); return candidates.OrderByDescending(x => x.DiscountAmount).ThenByDescending(x => x.Priority).ThenBy(x => x.PromotionId).FirstOrDefault();
    }
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
