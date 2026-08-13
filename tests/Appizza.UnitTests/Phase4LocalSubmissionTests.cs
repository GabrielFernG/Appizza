using Appizza.Table.Core;

namespace Appizza.UnitTests;

public sealed class Phase4LocalSubmissionTests
{
    [Fact]
    public async Task SubmissionUnknownRetainsStableKeysAndReconcilesToSubmitted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appizza-phase4-{Guid.NewGuid():N}.db3"); var db = new LocalStateDatabase(path); await db.InitializeAsync(); var context = new LocalContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); var cart = await db.GetOrCreateCartAsync(context, 1, 1, DateTime.UtcNow); var simulation = Guid.NewGuid(); await db.RecordSimulationAsync(Guid.ParseExact(cart.Id, "N"), simulation, "sha256-v1:test", DateTime.UtcNow.AddMinutes(5), false, "{}", DateTime.UtcNow); var client = Guid.NewGuid(); var key = Guid.NewGuid(); await db.BeginSubmissionAsync(Guid.ParseExact(cart.Id, "N"), client, key, DateTime.UtcNow); await db.MarkSubmissionUnknownAsync(Guid.ParseExact(cart.Id, "N"), DateTime.UtcNow); var unknown = await db.GetSubmissionStateAsync(Guid.ParseExact(cart.Id, "N")); Assert.Equal("submission_unknown", unknown.Status); Assert.Equal(client, unknown.ClientSubmissionId); Assert.Equal(key, unknown.IdempotencyKey); var order = Guid.NewGuid(); await db.MarkSubmittedAsync(Guid.ParseExact(cart.Id, "N"), order, "{}", DateTime.UtcNow); Assert.Equal(order, (await db.GetSubmissionStateAsync(Guid.ParseExact(cart.Id, "N"))).OrderId); await db.CloseAsync(); File.Delete(path);
    }

    [Fact]
    public async Task SubmissionStatesPersistAcrossRestartAndSessionNeverInheritsPreviousCart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appizza-phase4-states-{Guid.NewGuid():N}.db3");
        var firstContext = new LocalContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var db = new LocalStateDatabase(path); await db.InitializeAsync();
        var cart = await db.GetOrCreateCartAsync(firstContext, 1, 1, DateTime.UtcNow);
        Assert.Equal("active", cart.Status);
        await db.RecordSimulationAsync(Guid.ParseExact(cart.Id, "N"), Guid.NewGuid(), "sha256-v1:review", DateTime.UtcNow.AddMinutes(5), true, "{}", DateTime.UtcNow);
        Assert.Equal("requires_review", (await db.GetSubmissionStateAsync(Guid.ParseExact(cart.Id, "N"))).Status);
        await db.CloseAsync();

        db = new LocalStateDatabase(path); await db.InitializeAsync();
        var restored = await db.GetOrCreateCartAsync(firstContext, 1, 1, DateTime.UtcNow);
        Assert.Equal(cart.Id, restored.Id);
        var identities = await db.BeginSubmissionAsync(Guid.ParseExact(cart.Id, "N"), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal("submitting", (await db.GetSubmissionStateAsync(Guid.ParseExact(cart.Id, "N"))).Status);
        await db.MarkSubmissionUnknownAsync(Guid.ParseExact(cart.Id, "N"), DateTime.UtcNow);
        await db.CloseAsync();

        db = new LocalStateDatabase(path); await db.InitializeAsync();
        var unknown = await db.GetSubmissionStateAsync(Guid.ParseExact(cart.Id, "N"));
        Assert.Equal("submission_unknown", unknown.Status); Assert.Equal(identities.ClientSubmissionId, unknown.ClientSubmissionId); Assert.Equal(identities.IdempotencyKey, unknown.IdempotencyKey);
        var nextContext = firstContext with { SessionId = Guid.NewGuid() };
        var next = await db.GetOrCreateCartAsync(nextContext, 1, 1, DateTime.UtcNow);
        Assert.NotEqual(cart.Id, next.Id); Assert.Equal("session_mismatch", (await db.GetSubmissionStateAsync(Guid.ParseExact(cart.Id, "N"))).Status); Assert.Equal("active", next.Status);
        await db.CloseAsync(); File.Delete(path);
    }

    [Fact]
    public async Task ConcurrentDoubleTapKeepsOneSubmissionIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appizza-phase4-double-{Guid.NewGuid():N}.db3"); var db = new LocalStateDatabase(path); await db.InitializeAsync();
        var cart = await db.GetOrCreateCartAsync(new LocalContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), 1, 1, DateTime.UtcNow);
        await db.RecordSimulationAsync(Guid.ParseExact(cart.Id, "N"), Guid.NewGuid(), "sha256-v1:ok", DateTime.UtcNow.AddMinutes(5), false, "{}", DateTime.UtcNow);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(Guid ClientSubmissionId, Guid IdempotencyKey)> Tap() { await gate.Task; return await db.BeginSubmissionAsync(Guid.ParseExact(cart.Id, "N"), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow); }
        var first = Tap(); var second = Tap(); gate.SetResult(); var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0], results[1]); Assert.NotEqual(Guid.Empty, results[0].ClientSubmissionId); Assert.NotEqual(Guid.Empty, results[0].IdempotencyKey);
        await db.CloseAsync(); File.Delete(path);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("requires_review")]
    [InlineData("submitting")]
    [InlineData("submission_unknown")]
    [InlineData("submitted")]
    public async Task SessionChangeNeverPromotesPreviousCartAcrossEverySubmissionState(string state)
    {
        var path = Path.Combine(Path.GetTempPath(), $"appizza-phase4-session-{Guid.NewGuid():N}.db3"); var db = new LocalStateDatabase(path); await db.InitializeAsync(); var context = new LocalContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); var cart = await db.GetOrCreateCartAsync(context, 1, 1, DateTime.UtcNow); var id = Guid.ParseExact(cart.Id, "N");
        if (state != "active") await db.RecordSimulationAsync(id, Guid.NewGuid(), "sha256-v1:a", DateTime.UtcNow.AddMinutes(5), state == "requires_review", "{}", DateTime.UtcNow);
        if (state is "submitting" or "submission_unknown" or "submitted") await db.BeginSubmissionAsync(id, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        if (state == "submission_unknown") await db.MarkSubmissionUnknownAsync(id, DateTime.UtcNow); if (state == "submitted") await db.MarkSubmittedAsync(id, Guid.NewGuid(), "{}", DateTime.UtcNow); await db.CloseAsync();
        db = new LocalStateDatabase(path); await db.InitializeAsync(); var next = await db.GetOrCreateCartAsync(context with { SessionId = Guid.NewGuid() }, 1, 1, DateTime.UtcNow.AddMinutes(1)); Assert.NotEqual(cart.Id, next.Id); var old = await db.GetSubmissionStateAsync(id); Assert.Equal(state == "submitted" ? "submitted" : "session_mismatch", old.Status); Assert.Equal("active", next.Status); await db.CloseAsync(); File.Delete(path);
    }

    [Fact]
    public async Task UnknownSubmissionSurvivesRestartAndMissingResultWithoutChangingIdentities()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appizza-phase4-retry-{Guid.NewGuid():N}.db3"); var db = new LocalStateDatabase(path); await db.InitializeAsync(); var cart = await db.GetOrCreateCartAsync(new LocalContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), 1, 1, DateTime.UtcNow); var id = Guid.ParseExact(cart.Id, "N"); await db.RecordSimulationAsync(id, Guid.NewGuid(), "sha256-v1:a", DateTime.UtcNow.AddMinutes(5), false, "{}", DateTime.UtcNow); var original = await db.BeginSubmissionAsync(id, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow); await db.MarkSubmissionUnknownAsync(id, DateTime.UtcNow); await db.CloseAsync();
        db = new LocalStateDatabase(path); await db.InitializeAsync(); var missing = await db.GetSubmissionStateAsync(id); Assert.Equal("submission_unknown", missing.Status); var retry = await db.BeginSubmissionAsync(id, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow); Assert.Equal(original, retry); var order = Guid.NewGuid(); await db.MarkSubmittedAsync(id, order, "{}", DateTime.UtcNow); var final = await db.GetSubmissionStateAsync(id); Assert.Equal("submitted", final.Status); Assert.Equal(order, final.OrderId); Assert.Equal(original.ClientSubmissionId, final.ClientSubmissionId); Assert.Equal(original.IdempotencyKey, final.IdempotencyKey); await db.CloseAsync(); File.Delete(path);
    }
}
