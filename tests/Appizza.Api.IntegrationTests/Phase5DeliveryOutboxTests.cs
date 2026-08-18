using Appizza.Api;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

public sealed class Phase5DeliveryOutboxTests
{
    [Fact]
    public void DeliveryPermissionsAreRegisteredExactlyOnce()
    {
        var delivery = Phase4KitchenPermissions.All.Where(static code => code.StartsWith("kitchen.delivery.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["kitchen.delivery.confirm", "kitchen.delivery.resolve", "kitchen.delivery.send"], delivery.OrderBy(static code => code));
        Assert.Equal(3, delivery.Distinct(StringComparer.Ordinal).Count());
    }

    public static IEnumerable<object[]> DeliverySignalRMatrix()
    {
        yield return ["production-item-sent-to-table.v1", new[] { "DeliveryChanged", "OrderStatusChanged" }];
        yield return ["delivery-confirmation-requested.v1", new[] { "DeliveryChanged" }];
        yield return ["delivery-confirmed-by-customer.v1", new[] { "DeliveryChanged", "OrderStatusChanged" }];
        yield return ["delivery-confirmed-by-employee.v1", new[] { "DeliveryChanged", "OrderStatusChanged" }];
        yield return ["delivery-auto-confirmed.v1", new[] { "DeliveryChanged", "OrderStatusChanged" }];
        yield return ["production-item-delivered.v1", new[] { "OrderStatusChanged" }];
        yield return ["delivery-contested.v1", new[] { "DeliveryChanged", "OrderStatusChanged" }];
        yield return ["delivery-contest-resolved.v1", new[] { "DeliveryChanged", "OrderStatusChanged" }];
    }

    [Theory]
    [MemberData(nameof(DeliverySignalRMatrix))]
    public void DeliveryEventsMapToDocumentedSignalRMethods(string eventType, string[] expected) => Assert.Equal(expected, Phase4SignalRNotificationPublisher.MethodsFor(eventType));

    public static IEnumerable<object[]> DeliveryConsumerMatrix()
    {
        yield return ["production-item-sent-to-table.v1", new[] { "ordering-public-status-v1", "delivery-worker-v1", "kitchen-signalr-v1" }];
        yield return ["delivery-confirmation-requested.v1", new[] { "delivery-worker-v1", "kitchen-signalr-v1" }];
        yield return ["delivery-confirmed-by-customer.v1", new[] { "ordering-public-status-v1", "kitchen-signalr-v1" }];
        yield return ["delivery-confirmed-by-employee.v1", new[] { "ordering-public-status-v1", "kitchen-signalr-v1" }];
        yield return ["delivery-auto-confirmed.v1", new[] { "ordering-public-status-v1", "kitchen-signalr-v1" }];
        yield return ["production-item-delivered.v1", new[] { "ordering-completion-v1", "kitchen-signalr-v1" }];
        yield return ["delivery-contested.v1", new[] { "delivery-worker-v1", "kitchen-signalr-v1" }];
        yield return ["delivery-contest-resolved.v1", new[] { "ordering-public-status-v1", "kitchen-signalr-v1" }];
    }

    [Theory]
    [MemberData(nameof(DeliveryConsumerMatrix))]
    public void DeliveryEventResolvesExactlyTheNormativeConsumers(string eventType, string[] expected)
    {
        var actual = Phase4OutboxDispatcher.DeliveryConsumerRegistry[eventType];
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, actual.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DeliveryRegistryContainsExactlyTheEightNormativeEvents()
    {
        Assert.Equal(8, Phase4OutboxDispatcher.DeliveryConsumerRegistry.Count);
        Assert.Equal(
            DeliveryConsumerMatrix().Select(static row => (string)row[0]).OrderBy(static x => x),
            Phase4OutboxDispatcher.DeliveryConsumerRegistry.Keys.OrderBy(static x => x));
    }

}

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryOutboxIntegrationTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task DeliveryNonSignalRConsumersCreateInboxAndRemainPendingUntilSignalR()
    {
        fixture.OutboxHook.Reset();
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var before = fixture.CreateDbContext();
        var sentEvent = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        var deliveredEvent = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-delivered.v1");
        var contestedEvent = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "delivery-contested.v1");
        var productionBefore = await before.Set<ProductionItem>().AsNoTracking().SingleAsync(x => x.Id == scenario.ProductionItemId);
        var confirmationBefore = await before.Set<DeliveryConfirmation>().AsNoTracking().SingleAsync(x => x.Id == scenario.DeliveryConfirmationId);

        await Phase5OutboxDispatchTestHelper.DispatchUntilInboxAsync(fixture, sentEvent.Id, "ordering-public-status-v1");

        await using var after = fixture.CreateDbContext();
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-public-status-v1", sentEvent.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("delivery-worker-v1", sentEvent.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-completion-v1", deliveredEvent.Id));
        Assert.Equal(2, fixture.Notifications.Count(sentEvent.Id));
        Assert.Equal(["DeliveryChanged", "OrderStatusChanged"], fixture.Notifications.Messages(sentEvent.Id).Select(x => x.Method));
        Assert.All(fixture.Notifications.Messages(sentEvent.Id), message => Assert.Equal(scenario.EstablishmentId, message.Tenant));
        Assert.Equal(1, fixture.Notifications.Count(deliveredEvent.Id));
        Assert.Equal("OrderStatusChanged", Assert.Single(fixture.Notifications.Messages(deliveredEvent.Id)).Method);
        Assert.Equal(2, fixture.Notifications.Count(contestedEvent.Id));
        Assert.Equal(["DeliveryChanged", "OrderStatusChanged"], fixture.Notifications.Messages(contestedEvent.Id).Select(x => x.Method));
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == sentEvent.Id && x.ConsumerName == "ordering-public-status-v1"));
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == sentEvent.Id && x.ConsumerName == "delivery-worker-v1"));
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == deliveredEvent.Id && x.ConsumerName == "ordering-completion-v1"));
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == sentEvent.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == sentEvent.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == deliveredEvent.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == contestedEvent.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == contestedEvent.Id).Select(x => x.ProcessedAt).SingleAsync());
        var productionAfter = await after.Set<ProductionItem>().AsNoTracking().SingleAsync(x => x.Id == scenario.ProductionItemId);
        Assert.Equal(productionBefore.Status, productionAfter.Status);
        Assert.Equal(productionBefore.Version, productionAfter.Version);
        var confirmationAfter = await after.Set<DeliveryConfirmation>().AsNoTracking().SingleAsync(x => x.Id == scenario.DeliveryConfirmationId);
        Assert.Equal(confirmationBefore.Status, confirmationAfter.Status);
        Assert.Equal(confirmationBefore.Version, confirmationAfter.Version);

        await fixture.DispatchPhase4Async();
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-public-status-v1", sentEvent.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("delivery-worker-v1", sentEvent.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-completion-v1", deliveredEvent.Id));
        Assert.Equal(2, fixture.Notifications.Count(sentEvent.Id));
        await using var replay = fixture.CreateDbContext();
        Assert.NotNull(await replay.OutboxMessages.Where(x => x.Id == sentEvent.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(3, await replay.InboxMessages.CountAsync(x => x.EventId == sentEvent.Id));
    }

    [Fact]
    public async Task DeliveryConsumersAreFinanciallyInertAndTenantScoped()
    {
        fixture.OutboxHook.Reset();
        var scenarioA = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var scenarioB = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var before = fixture.CreateDbContext();
        var snapshotA = await SnapshotAsync(before, scenarioA);
        var snapshotB = await SnapshotAsync(before, scenarioB);
        var eventA = await before.OutboxMessages.SingleAsync(x => x.Id != Guid.Empty && x.EstablishmentId == scenarioA.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        var eventB = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenarioB.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        Assert.NotEqual(eventA.Id, eventB.Id);
        Phase5OutboxDiagnostics.Print("before sentinel dispatch", await Phase5OutboxDiagnostics.CaptureAsync(before, eventA.Id));

        await Phase5OutboxDispatchTestHelper.DispatchUntilInboxAsync(fixture, eventA.Id, "ordering-public-status-v1");

        await using var after = fixture.CreateDbContext();
        var afterA = await SnapshotAsync(after, scenarioA);
        var afterB = await SnapshotAsync(after, scenarioB);
        var sentinelInboxPresent = await after.InboxMessages.AnyAsync(x => x.EventId == eventA.Id && x.ConsumerName == "ordering-public-status-v1");
        if (!sentinelInboxPresent) Phase5OutboxDiagnostics.Print("sentinel inbox absent after dispatch", await Phase5OutboxDiagnostics.CaptureAsync(after, eventA.Id));
        AssertFinancialEqual(snapshotA, afterA);
        AssertFinancialEqual(snapshotB, afterB);
        Assert.True(sentinelInboxPresent);
        Assert.False(await after.InboxMessages.AnyAsync(x => x.EventId == eventB.Id && x.ConsumerName == "ordering-public-status-v1" && x.EventId == eventA.Id));
        Assert.All(await after.InboxMessages.Where(x => x.EventId == eventA.Id).ToListAsync(), row => Assert.Equal(eventA.Id, row.EventId));
        Assert.DoesNotContain(await after.InboxMessages.Where(x => x.EventId == eventA.Id).Select(x => x.EventId).ToListAsync(), id => id == eventB.Id);
    }

    [Fact]
    public async Task SignalRFailureLeavesItsInboxAbsentAndOutboxUnprocessed()
    {
        fixture.OutboxHook.Reset(); fixture.Notifications.Reset(); fixture.Notifications.Fail = true;
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var before = fixture.CreateDbContext();
        var sentEvent = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        await fixture.DispatchPhase4Async();
        await using var after = fixture.CreateDbContext();
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == sentEvent.Id && x.ConsumerName == "ordering-public-status-v1"));
        Assert.True(await after.InboxMessages.AnyAsync(x => x.EventId == sentEvent.Id && x.ConsumerName == "delivery-worker-v1"));
        Assert.False(await after.InboxMessages.AnyAsync(x => x.EventId == sentEvent.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.Null(await after.OutboxMessages.Where(x => x.Id == sentEvent.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.True((await after.OutboxMessages.Where(x => x.Id == sentEvent.Id).Select(x => x.RetryCount).SingleAsync()) > 0);
        fixture.Notifications.Fail = false;
    }

    [Fact]
    public async Task DeliverySignalRFailureRetriesOnlyPendingConsumer()
    {
        fixture.OutboxHook.Reset(); fixture.Notifications.Reset();
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var before = fixture.CreateDbContext();
        var message = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        fixture.Notifications.FailEvent(message.Id);
        await fixture.DispatchPhase4Async();
        await using var failed = fixture.CreateDbContext();
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-public-status-v1", message.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("delivery-worker-v1", message.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.True(await failed.InboxMessages.AnyAsync(x => x.EventId == message.Id && x.ConsumerName == "ordering-public-status-v1"));
        Assert.True(await failed.InboxMessages.AnyAsync(x => x.EventId == message.Id && x.ConsumerName == "delivery-worker-v1"));
        Assert.False(await failed.InboxMessages.AnyAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));
        var failedOutbox = await failed.OutboxMessages.SingleAsync(x => x.Id == message.Id);
        Assert.Null(failedOutbox.ProcessedAt); Assert.Equal(1, failedOutbox.RetryCount); Assert.NotNull(failedOutbox.NextRetryAt); Assert.Contains("testing-delivery-signalr-failure", failedOutbox.ErrorMessage);
        fixture.Notifications.Reset();
        await fixture.DispatchPhase4Async();
        await using var retried = fixture.CreateDbContext();
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-public-status-v1", message.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("delivery-worker-v1", message.Id));
        Assert.Equal(2, fixture.OutboxHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.Equal(1, await retried.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await retried.OutboxMessages.Where(x => x.Id == message.Id).Select(x => x.ProcessedAt).SingleAsync());
        await fixture.DispatchPhase4Async();
        Assert.Equal(2, fixture.OutboxHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
    }

    [Fact]
    public async Task DeliveredSignalRFailureRetriesOnlyPendingSignalR()
    {
        fixture.OutboxHook.Reset(); fixture.Notifications.Reset();
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var before = fixture.CreateDbContext();
        var message = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-delivered.v1");
        fixture.Notifications.FailEvent(message.Id);
        await fixture.DispatchPhase4Async();
        await using var failed = fixture.CreateDbContext();
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-completion-v1", message.Id));
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.True(await failed.InboxMessages.AnyAsync(x => x.EventId == message.Id && x.ConsumerName == "ordering-completion-v1"));
        Assert.False(await failed.InboxMessages.AnyAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.Null(await failed.OutboxMessages.Where(x => x.Id == message.Id).Select(x => x.ProcessedAt).SingleAsync());
        fixture.Notifications.Reset(); await fixture.DispatchPhase4Async();
        await using var retried = fixture.CreateDbContext();
        Assert.Equal(1, fixture.OutboxHook.GetInvocationCount("ordering-completion-v1", message.Id));
        Assert.Equal(2, fixture.OutboxHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.Equal(1, await retried.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await retried.OutboxMessages.Where(x => x.Id == message.Id).Select(x => x.ProcessedAt).SingleAsync());
    }

    [Fact]
    public async Task DeliveryDispatcherRestartResumesOnlyPendingConsumer()
    {
        fixture.OutboxHook.Reset(); fixture.Notifications.Reset();
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var before = fixture.CreateDbContext();
        var message = await before.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        fixture.Notifications.FailEvent(message.Id);
        var firstHook = new Phase5OutboxTestHook();
        await fixture.CreateDispatcher(firstHook).DispatchOnceAsync(CancellationToken.None);
        await using var partial = fixture.CreateDbContext();
        Assert.Null(await partial.OutboxMessages.Where(x => x.Id == message.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(1, await partial.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "ordering-public-status-v1"));
        Assert.Equal(1, await partial.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "delivery-worker-v1"));
        Assert.Equal(0, await partial.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));

        fixture.Notifications.Reset();
        var restartedHook = new Phase5OutboxTestHook();
        await fixture.CreateDispatcher(restartedHook).DispatchOnceAsync(CancellationToken.None);
        await using var completed = fixture.CreateDbContext();
        Assert.Equal(0, restartedHook.GetInvocationCount("ordering-public-status-v1", message.Id));
        Assert.Equal(0, restartedHook.GetInvocationCount("delivery-worker-v1", message.Id));
        Assert.Equal(1, restartedHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.Equal(1, await completed.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await completed.OutboxMessages.Where(x => x.Id == message.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(2, fixture.Notifications.Count(message.Id));

        var terminalHook = new Phase5OutboxTestHook();
        await fixture.CreateDispatcher(terminalHook).DispatchOnceAsync(CancellationToken.None);
        Assert.Equal(0, terminalHook.GetInvocationCount("ordering-public-status-v1", message.Id));
        Assert.Equal(0, terminalHook.GetInvocationCount("delivery-worker-v1", message.Id));
        Assert.Equal(0, terminalHook.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.Equal(2, fixture.Notifications.Count(message.Id));
    }

    [Fact]
    public async Task TwoDispatchersProcessDeliveryConsumersExactlyOnce()
    {
        fixture.OutboxHook.Reset(); fixture.Notifications.Reset();
        await Phase5OutboxDispatchTestHelper.DrainEligibleBacklogAsync(fixture);
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var db = fixture.CreateDbContext();
        var message = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        var delivered = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "production-item-delivered.v1");
        var hookA = new Phase5OutboxTestHook();
        var hookB = new Phase5OutboxTestHook();
        var dispatcherA = fixture.CreateDispatcher(hookA);
        var dispatcherB = fixture.CreateDispatcher(hookB);
        await Task.WhenAll(dispatcherA.DispatchOnceAsync(CancellationToken.None), dispatcherB.DispatchOnceAsync(CancellationToken.None));

        await using var after = fixture.CreateDbContext();
        Assert.Equal(1, await after.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "ordering-public-status-v1"));
        Assert.Equal(1, await after.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "delivery-worker-v1"));
        Assert.Equal(1, await after.InboxMessages.CountAsync(x => x.EventId == message.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == message.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(1, hookA.GetInvocationCount("ordering-public-status-v1", message.Id) + hookB.GetInvocationCount("ordering-public-status-v1", message.Id));
        Assert.Equal(1, hookA.GetInvocationCount("delivery-worker-v1", message.Id) + hookB.GetInvocationCount("delivery-worker-v1", message.Id));
        Assert.Equal(1, hookA.GetInvocationCount("kitchen-signalr-v1", message.Id) + hookB.GetInvocationCount("kitchen-signalr-v1", message.Id));
        Assert.Equal(2, fixture.Notifications.Count(message.Id));
        Assert.Equal(1, await after.InboxMessages.CountAsync(x => x.EventId == delivered.Id && x.ConsumerName == "ordering-completion-v1"));
        Assert.Equal(1, await after.InboxMessages.CountAsync(x => x.EventId == delivered.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.NotNull(await after.OutboxMessages.Where(x => x.Id == delivered.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(1, hookA.GetInvocationCount("ordering-completion-v1", delivered.Id) + hookB.GetInvocationCount("ordering-completion-v1", delivered.Id));
        Assert.Equal(1, hookA.GetInvocationCount("kitchen-signalr-v1", delivered.Id) + hookB.GetInvocationCount("kitchen-signalr-v1", delivered.Id));
        Assert.Equal(1, fixture.Notifications.Count(delivered.Id));
    }

    [Fact]
    public async Task DeliveryEventsRemainIsolatedAcrossTenantsAndRetry()
    {
        fixture.OutboxHook.Reset(); fixture.Notifications.Reset();
        var scenarioA = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var scenarioB = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using var db = fixture.CreateDbContext();
        var eventA = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenarioA.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        var eventB = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == scenarioB.EstablishmentId && x.EventType == "production-item-sent-to-table.v1");
        Assert.NotEqual(eventA.EstablishmentId, eventB.EstablishmentId);
        fixture.Notifications.FailEvent(eventA.Id);
        await fixture.DispatchPhase4Async();
        await using var failed = fixture.CreateDbContext();
        Assert.Null(await failed.OutboxMessages.Where(x => x.Id == eventA.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.NotNull(await failed.OutboxMessages.Where(x => x.Id == eventB.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(0, await failed.InboxMessages.CountAsync(x => x.EventId == eventA.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.Equal(1, await failed.InboxMessages.CountAsync(x => x.EventId == eventB.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.All(fixture.Notifications.Messages(eventB.Id), x => Assert.Equal(eventB.EstablishmentId, x.Tenant));
        Assert.DoesNotContain(fixture.Notifications.Messages(eventB.Id), x => x.Tenant == eventA.EstablishmentId);

        fixture.Notifications.Reset();
        await fixture.DispatchPhase4Async();
        await using var retried = fixture.CreateDbContext();
        Assert.NotNull(await retried.OutboxMessages.Where(x => x.Id == eventA.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.NotNull(await retried.OutboxMessages.Where(x => x.Id == eventB.Id).Select(x => x.ProcessedAt).SingleAsync());
        Assert.Equal(1, await retried.InboxMessages.CountAsync(x => x.EventId == eventA.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.Equal(1, await retried.InboxMessages.CountAsync(x => x.EventId == eventB.Id && x.ConsumerName == "kitchen-signalr-v1"));
        Assert.All(fixture.Notifications.Messages(eventA.Id), x => Assert.Equal(eventA.EstablishmentId, x.Tenant));
        Assert.Empty(fixture.Notifications.Messages(eventB.Id));
    }

    private static async Task<FinancialDeliverySnapshot> SnapshotAsync(AppizzaDbContext db, Phase5DeliveryContestScenario scenario)
    {
        var item = await db.Set<OrderItem>().AsNoTracking().SingleAsync(x => x.Id == scenario.OrderItemId);
        var order = await db.Set<Order>().AsNoTracking().SingleAsync(x => x.Id == scenario.OrderId);
        var session = await db.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == order.TableSessionId);
        var revisions = await db.Set<OrderItemRevision>().AsNoTracking().Where(x => x.OrderItemId == item.Id).OrderBy(x => x.RevisionNumber).Select(x => new RevisionSnapshot(x.Id, x.RevisionNumber, x.PreviousUnitAmount, x.UnitAmount, x.PreviousTotalAmount, x.TotalAmount, x.PriceDifference)).ToListAsync();
        return new(item.UnitAmount, item.TotalAmount, item.CurrentRevisionNumber, revisions, order.SubtotalAmount, order.TotalAmount, session.SubtotalAmount, session.TotalAmount);
    }

    private static void AssertFinancialEqual(FinancialDeliverySnapshot expected, FinancialDeliverySnapshot actual)
    {
        Assert.Equal(expected.UnitAmount, actual.UnitAmount);
        Assert.Equal(expected.TotalAmount, actual.TotalAmount);
        Assert.Equal(expected.CurrentRevisionNumber, actual.CurrentRevisionNumber);
        Assert.Equal(expected.OrderSubtotal, actual.OrderSubtotal);
        Assert.Equal(expected.OrderTotal, actual.OrderTotal);
        Assert.Equal(expected.SessionSubtotal, actual.SessionSubtotal);
        Assert.Equal(expected.SessionTotal, actual.SessionTotal);
        Assert.Equal(expected.Revisions, actual.Revisions);
    }

    private sealed record FinancialDeliverySnapshot(decimal UnitAmount, decimal TotalAmount, int CurrentRevisionNumber, IReadOnlyList<RevisionSnapshot> Revisions, decimal OrderSubtotal, decimal OrderTotal, decimal SessionSubtotal, decimal SessionTotal);
    private sealed record RevisionSnapshot(Guid Id, int Number, decimal PreviousUnit, decimal Unit, decimal PreviousTotal, decimal Total, decimal Difference);
}
