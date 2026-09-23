using System.Net;
using System.Text.Json;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7OperationsClosingListApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ClosingListReturnsOnlyTenantClosingSessions()
    {
        var own = await fixture.CreateOpenSessionAsync(); var foreign = await fixture.CreateOpenSessionAsync();
        await SetStatus(own.SessionId, "awaiting_payment"); await SetStatus(foreign.SessionId, "closing");
        var tenant = await Tenant(own.SessionId); var token = await fixture.CreateUserTokenAsync(tenant, "closing.view");
        var response = await fixture.GetAsync("api/v1/operations/sessions/closing", token); response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var ids = json.RootElement.EnumerateArray().Select(x => x.GetProperty("sessionId").GetGuid()).ToArray();
        Assert.Contains(own.SessionId, ids); Assert.DoesNotContain(foreign.SessionId, ids);
    }

    [Fact]
    public async Task ClosingListExcludesOpenSessionsAndSupportsEmptyState()
    {
        var session = await fixture.CreateOpenSessionAsync(); var tenant = await Tenant(session.SessionId); var token = await fixture.CreateUserTokenAsync(tenant, "closing.view");
        var response = await fixture.GetAsync("api/v1/operations/sessions/closing", token); response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task ClosingListRequiresClosingView()
    {
        var session = await fixture.CreateOpenSessionAsync(); var response = await fixture.GetAsync("api/v1/operations/sessions/closing", await fixture.CreateUserTokenAsync(await Tenant(session.SessionId)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> Tenant(Guid sessionId) { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(x => x.Id == sessionId).Select(x => x.EstablishmentId).SingleAsync(); }
    private async Task SetStatus(Guid sessionId, string status) { await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId); s.Status = status; await db.SaveChangesAsync(); }
}
