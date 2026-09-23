using System.Security.Claims;
using Appizza.Modules.Identity;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class OperationsClosingListEndpoints
{
    public static IEndpointRouteBuilder MapOperationsClosingListEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/operations/sessions/closing", List).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        if (!principal.IsTokenType("user")) return Results.Problem(statusCode: 403, title: "INVALID_TOKEN_TYPE");
        var tenant = principal.RequiredGuid("establishment_id");
        var user = principal.RequiredGuid("sub");
        if (!await db.Set<User>().AnyAsync(x => x.Id == user && x.EstablishmentId == tenant && x.Status == "active", ct)) return Results.NotFound();
        var permissions = await PermissionResolver.ResolveAsync(db, user, DateTimeOffset.UtcNow, ct);
        if (!permissions.Contains("closing.view")) return Results.Problem(statusCode: 403, title: "INSUFFICIENT_PERMISSION");
        var states = new[] { "closing", "awaiting_payment" };
        var sessions = await db.Set<TableSession>().AsNoTracking().Where(x => x.EstablishmentId == tenant && states.Contains(x.Status)).OrderBy(x => x.OpenedAt).Select(x => new { sessionId = x.Id, status = x.Status, version = x.Version, openedAt = x.OpenedAt, closingStartedAt = x.ClosingStartedAt, diningTableId = x.DiningTableId }).ToListAsync(ct);
        return Results.Ok(sessions);
    }
}
