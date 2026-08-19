using System.Security.Claims;
using System.Text.Json;
using Appizza.Modules.Promotions;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase6PromotionEndpoints
{
    public static IEndpointRouteBuilder MapPhase6PromotionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/operations/promotions").RequireAuthorization();
        g.MapGet("", List); g.MapPost("", Create); g.MapPost("/{id:guid}/activate", Activate); g.MapPost("/{id:guid}/pause", Pause);
        return app;
    }
    private static async Task<IResult> List(ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) { var denied = await Auth(p, db, "promotions.view", ct); if (denied is not null) return denied; var t = p.RequiredGuid("establishment_id"); return Results.Ok(await db.Set<Promotion>().AsNoTracking().Where(x => x.EstablishmentId == t).OrderBy(x => x.Name).ToListAsync(ct)); }
    private static async Task<IResult> Create(CreatePromotionRequest r, HttpRequest http, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct)
    { var denied = await Auth(p, db, "promotions.create", ct); if (denied is not null) return denied; if (!Guid.TryParse(http.Headers["Idempotency-Key"], out _)) return Err(400,"IDEMPOTENCY_KEY_REQUIRED"); if (r.Kind is not (PromotionKinds.Percentage or PromotionKinds.FixedAmount) || r.Scope is not (PromotionScopes.EntireOrder or PromotionScopes.SpecificProducts) || r.Value < 0 || r.StartsAt >= r.EndsAt) return Err(400,"PROMOTION_INVALID"); if (r.Scope == PromotionScopes.SpecificProducts && (r.ProductIds is null || r.ProductIds.Count == 0)) return Err(400,"PROMOTION_PRODUCTS_REQUIRED"); var t = p.RequiredGuid("establishment_id"); var now = DateTimeOffset.UtcNow; var entity = new Promotion { Id=Guid.NewGuid(), EstablishmentId=t, Name=r.Name.Trim(), Priority=r.Priority, Status="draft", CreatedAt=now, UpdatedAt=now }; var version = new PromotionVersion { Id=Guid.NewGuid(), PromotionId=entity.Id, EstablishmentId=t, Kind=r.Kind, Scope=r.Scope, Value=r.Value, EligibleProductIds=JsonSerializer.Serialize(r.ProductIds ?? []), StartsAt=r.StartsAt, EndsAt=r.EndsAt, CreatedAt=now }; entity.CurrentVersionId=version.Id; db.Add(entity); db.Add(version); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/operations/promotions/{entity.Id}", new { entity.Id, entity.Name, entity.Status, version }); }
    private static async Task<IResult> Activate(Guid id, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) => await Transition(id, "active", p, db, "promotions.activate", ct);
    private static async Task<IResult> Pause(Guid id, ClaimsPrincipal p, AppizzaDbContext db, CancellationToken ct) => await Transition(id, "inactive", p, db, "promotions.edit", ct);
    private static async Task<IResult> Transition(Guid id, string status, ClaimsPrincipal p, AppizzaDbContext db, string permission, CancellationToken ct) { var denied=await Auth(p,db,permission,ct); if(denied is not null)return denied; var t=p.RequiredGuid("establishment_id"); var x=await db.Set<Promotion>().SingleOrDefaultAsync(a=>a.Id==id&&a.EstablishmentId==t,ct); if(x is null)return Results.NotFound(); if(status=="active" && x.CurrentVersionId is null)return Err(409,"PROMOTION_VERSION_REQUIRED"); if(x.Status=="expired")return Err(409,"PROMOTION_TERMINAL"); x.Status=status; x.UpdatedAt=DateTimeOffset.UtcNow; db.Add(new OutboxMessage{Id=Guid.NewGuid(),EstablishmentId=t,EventType=status=="active"?"promotion-activated.v1":"promotion-paused.v1",SchemaVersion=1,OccurredAt=DateTimeOffset.UtcNow,Payload=JsonSerializer.Serialize(new{promotionId=id,status})}); await db.SaveChangesAsync(ct); return Results.Ok(new{x.Id,x.Status,x.Version}); }
    private static async Task<IResult?> Auth(ClaimsPrincipal p, AppizzaDbContext db, string permission, CancellationToken ct) { if(!p.IsTokenType("user"))return Err(403,"INVALID_TOKEN_TYPE"); var user=p.RequiredGuid("sub"); var tenant=p.RequiredGuid("establishment_id"); if(!await db.Set<Appizza.Modules.Identity.User>().AnyAsync(x=>x.Id==user&&x.EstablishmentId==tenant&&x.Status=="active",ct))return Results.NotFound(); var permissions=await PermissionResolver.ResolveAsync(db,user,DateTimeOffset.UtcNow,ct); return permissions.Contains(permission)?null:Err(403,"INSUFFICIENT_PERMISSION"); }
    private static IResult Err(int status,string code)=>Results.Problem(statusCode:status,title:code,detail:code,extensions:new Dictionary<string,object?> { ["errorCode"] = code });
    private sealed record CreatePromotionRequest(string Name,string Kind,string Scope,decimal Value,DateTimeOffset StartsAt,DateTimeOffset EndsAt,int Priority,List<Guid>? ProductIds);
}
