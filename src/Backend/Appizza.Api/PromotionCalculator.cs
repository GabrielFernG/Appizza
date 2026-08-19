using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Appizza.Modules.Ordering;
using Appizza.Modules.Promotions;

namespace Appizza.Api;

public static class PromotionCalculator
{
    public static async Task<PromotionDiscount?> SelectAsync(Appizza.Persistence.AppizzaDbContext db, Guid establishmentId, IReadOnlyList<AuthoritativeCartItem> items, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (from p in db.Set<Promotion>().AsNoTracking() join v in db.Set<PromotionVersion>().AsNoTracking() on p.CurrentVersionId equals v.Id where p.EstablishmentId == establishmentId && p.Status == "active" && v.EstablishmentId == establishmentId && v.StartsAt <= now && v.EndsAt > now select new { p.Id, p.Name, p.Priority, v }).ToListAsync(ct);
        var candidates = rows.Select(row => new PromotionCandidate(row.Id, row.v.Id, row.Name, row.v.Kind, row.v.Scope, row.v.Value, row.Priority, JsonSerializer.Deserialize<HashSet<Guid>>(row.v.EligibleProductIds) ?? []));
        return PromotionRules.Select(candidates, items.Select(x => new PromotionItem(x.ProductId, x.TotalAmount)));
    }
}
