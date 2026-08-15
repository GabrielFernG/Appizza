using Appizza.Persistence;
using Appizza.Modules.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5ChangeOutboxTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task K1HappyPathProcessesEachConsumerOnceAndIsIdempotent()
    {
        var order = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync();
        var change = await new Phase5ChangeScenarioBuilder(fixture).BuildAsync(order);
        var hook = fixture.OutboxHook;
        hook.Reset();

        await using (var before = fixture.CreateDbContext())
        {
            var message = await before.OutboxMessages.SingleAsync(x => x.Id == change.OrderItemChangedEventId && x.EstablishmentId == order.EstablishmentId);
            Assert.Null(message.ProcessedAt);
            Assert.Empty(await before.InboxMessages.Where(x => x.EventId == message.Id && (x.ConsumerName == "kitchen-item-change-v1" || x.ConsumerName == "ordering-signalr-v1")).ToListAsync());
        }

        await fixture.DispatchPhase4Async();

        await using (var after = fixture.CreateDbContext())
        {
            var consumers = await after.InboxMessages.Where(x => x.EventId == change.OrderItemChangedEventId).Select(x => x.ConsumerName).ToListAsync();
            Assert.Equal(["kitchen-item-change-v1", "ordering-signalr-v1"], consumers.OrderBy(x => x).ToArray());
            Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == change.OrderItemChangedEventId).Select(x => x.ProcessedAt).SingleAsync());
        }

        Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
        Assert.Equal(1, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));

        await fixture.DispatchPhase4Async();

        await using (var replay = fixture.CreateDbContext())
        {
            Assert.Equal(2, await replay.InboxMessages.CountAsync(x => x.EventId == change.OrderItemChangedEventId));
            Assert.NotNull(await replay.OutboxMessages.Where(x => x.Id == change.OrderItemChangedEventId).Select(x => x.ProcessedAt).SingleAsync());
        }
        Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
        Assert.Equal(1, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));
        hook.Reset();
    }

    [Fact]
    public async Task K2SignalRFailureLeavesKitchenCompletedAndOutboxPending()
    {
        var order = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync();
        var change = await new Phase5ChangeScenarioBuilder(fixture).BuildAsync(order);
        var hook = fixture.OutboxHook;
        hook.Reset();
        var failure = new InvalidOperationException("testing-signalr-failure");
        hook.FailNext("ordering-signalr-v1", change.OrderItemChangedEventId, failure);
        try
        {
            await using (var before = fixture.CreateDbContext())
            {
                var message = await before.OutboxMessages.SingleAsync(x => x.Id == change.OrderItemChangedEventId && x.EstablishmentId == order.EstablishmentId);
                Assert.Null(message.ProcessedAt);
                Assert.Empty(await before.InboxMessages.Where(x => x.EventId == message.Id && (x.ConsumerName == "kitchen-item-change-v1" || x.ConsumerName == "ordering-signalr-v1")).ToListAsync());
            }

            await fixture.DispatchPhase4Async();

            await using var db = fixture.CreateDbContext();
            var outbox = await db.OutboxMessages.SingleAsync(x => x.Id == change.OrderItemChangedEventId && x.EstablishmentId == order.EstablishmentId);
            Assert.Null(outbox.ProcessedAt);
            Assert.Equal(1, outbox.RetryCount);
            Assert.NotNull(outbox.NextRetryAt);
            Assert.Contains("testing-signalr-failure", outbox.ErrorMessage, StringComparison.Ordinal);
            Assert.True(await db.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "kitchen-item-change-v1"));
            Assert.False(await db.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "ordering-signalr-v1"));
            Assert.Equal(1, await db.Set<Appizza.Modules.Kitchen.ProductionItem>().CountAsync(x => x.OrderItemId == order.OrderItemId));
            Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
            Assert.Equal(1, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));
        }
        finally
        {
            hook.Reset();
        }
    }

    [Fact]
    public async Task K3RetryProcessesOnlyTheFailedConsumer()
    {
        var order = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync();
        var change = await new Phase5ChangeScenarioBuilder(fixture).BuildAsync(order);
        var hook = fixture.OutboxHook;
        hook.Reset();
        hook.FailNext("ordering-signalr-v1", change.OrderItemChangedEventId, new InvalidOperationException("testing-signalr-failure"));
        try
        {
            await fixture.DispatchPhase4Async();
            await using (var failed = fixture.CreateDbContext())
            {
                var outbox = await failed.OutboxMessages.SingleAsync(x => x.Id == change.OrderItemChangedEventId && x.EstablishmentId == order.EstablishmentId);
                Assert.Null(outbox.ProcessedAt);
                Assert.Equal(1, outbox.RetryCount);
                Assert.NotNull(outbox.NextRetryAt);
                Assert.Contains("testing-signalr-failure", outbox.ErrorMessage, StringComparison.Ordinal);
                Assert.True(await failed.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "kitchen-item-change-v1"));
                Assert.False(await failed.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "ordering-signalr-v1"));
            }
            Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
            Assert.Equal(1, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));

            // The dispatcher currently selects all unprocessed messages; K3 uses that deterministic eligibility rule.
            await fixture.DispatchPhase4Async();
            await using (var retried = fixture.CreateDbContext())
            {
                var outbox = await retried.OutboxMessages.SingleAsync(x => x.Id == change.OrderItemChangedEventId && x.EstablishmentId == order.EstablishmentId);
                Assert.NotNull(outbox.ProcessedAt);
                Assert.Equal(1, outbox.RetryCount);
                Assert.True(await retried.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "kitchen-item-change-v1"));
                Assert.True(await retried.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "ordering-signalr-v1"));
                Assert.Equal(2, await retried.InboxMessages.CountAsync(x => x.EventId == change.OrderItemChangedEventId));
                Assert.Equal(1, await retried.Set<OrderItemRevision>().CountAsync(x => x.OrderItemId == order.OrderItemId));
                Assert.Equal(1, await retried.Set<Appizza.Modules.Kitchen.ProductionItem>().CountAsync(x => x.OrderItemId == order.OrderItemId));
            }
            Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
            Assert.Equal(2, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));

            await fixture.DispatchPhase4Async();
            await using var terminal = fixture.CreateDbContext();
            Assert.NotNull(await terminal.OutboxMessages.Where(x => x.Id == change.OrderItemChangedEventId).Select(x => x.ProcessedAt).SingleAsync());
            Assert.Equal(2, await terminal.InboxMessages.CountAsync(x => x.EventId == change.OrderItemChangedEventId));
            Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
            Assert.Equal(2, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));
        }
        finally
        {
            hook.Reset();
        }
    }

    [Fact]
    public async Task K4LogicalRestartResumesFromPersistedInbox()
    {
        var order = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync();
        var change = await new Phase5ChangeScenarioBuilder(fixture).BuildAsync(order);
        var firstHook = fixture.OutboxHook;
        firstHook.Reset();
        firstHook.FailNext("ordering-signalr-v1", change.OrderItemChangedEventId, new InvalidOperationException("testing-signalr-failure"));
        await fixture.DispatchPhase4Async();
        await using (var partial = fixture.CreateDbContext())
        {
            Assert.Null(await partial.OutboxMessages.Where(x => x.Id == change.OrderItemChangedEventId).Select(x => x.ProcessedAt).SingleAsync());
            Assert.True(await partial.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "kitchen-item-change-v1"));
            Assert.False(await partial.InboxMessages.AnyAsync(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "ordering-signalr-v1"));
        }

        var restartedHook = new Phase5OutboxTestHook();
        var restartedDispatcher = fixture.CreateDispatcher(restartedHook);
        await restartedDispatcher.DispatchOnceAsync(CancellationToken.None);
        await using var after = fixture.CreateDbContext();
        Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == change.OrderItemChangedEventId).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(2, await after.InboxMessages.CountAsync(x => x.EventId == change.OrderItemChangedEventId));
        Assert.Equal(0, restartedHook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
        Assert.Equal(1, restartedHook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));
        Assert.Equal(1, await after.Set<Appizza.Modules.Kitchen.ProductionItem>().CountAsync(x => x.OrderItemId == order.OrderItemId));
        restartedHook.Reset();
        firstHook.Reset();
    }

    [Fact]
    public async Task K4ConcurrentDispatchersProcessEachConsumerOnce()
    {
        var order = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync();
        var change = await new Phase5ChangeScenarioBuilder(fixture).BuildAsync(order);
        var hook = fixture.OutboxHook;
        hook.Reset();
        hook.BlockNext("kitchen-item-change-v1", change.OrderItemChangedEventId);
        var dispatcherA = fixture.CreateDispatcher(hook);
        var dispatcherB = fixture.CreateDispatcher(hook);
        var dispatchA = dispatcherA.DispatchOnceAsync(CancellationToken.None);
        await hook.WaitUntilReachedAsync("kitchen-item-change-v1", change.OrderItemChangedEventId);
        var dispatchB = dispatcherB.DispatchOnceAsync(CancellationToken.None);
        hook.Release("kitchen-item-change-v1", change.OrderItemChangedEventId);
        await Task.WhenAll(dispatchA, dispatchB);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.InboxMessages.CountAsync(x => x.EventId == change.OrderItemChangedEventId));
        Assert.NotNull(await db.OutboxMessages.Where(x => x.Id == change.OrderItemChangedEventId).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(1, await db.Set<Appizza.Modules.Kitchen.ProductionItem>().CountAsync(x => x.OrderItemId == order.OrderItemId));
        Assert.Equal(1, await db.Set<OrderItemRevision>().CountAsync(x => x.OrderItemId == order.OrderItemId));
        Assert.Equal(1, hook.GetInvocationCount("kitchen-item-change-v1", change.OrderItemChangedEventId));
        Assert.Equal(1, hook.GetInvocationCount("ordering-signalr-v1", change.OrderItemChangedEventId));
        hook.Reset();
    }
}
