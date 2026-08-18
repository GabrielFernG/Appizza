using Appizza.Persistence;

namespace Appizza.UnitTests;

public sealed class Phase5DeliveryConcurrencyHookTests
{
    [Fact]
    public async Task OperationsAreIndependent()
    {
        var hook = new Phase5DeliveryConcurrencyHook();
        var id = Guid.NewGuid();
        hook.BlockNext("resolve-before-locks", id, "retry_delivery");
        var retry = hook.ReachAsync("resolve-before-locks", id, "retry_delivery", CancellationToken.None);
        await hook.WaitUntilReachedAsync("resolve-before-locks", id, "retry_delivery");
        var confirm = hook.ReachAsync("resolve-before-locks", id, "confirm_delivered", CancellationToken.None);
        await confirm;
        Assert.False(retry.IsCompleted);
        hook.Release("resolve-before-locks", id, "retry_delivery");
        await retry;
    }

    [Fact]
    public async Task ResetReleasesPendingGateAndClearsCounts()
    {
        var hook = new Phase5DeliveryConcurrencyHook();
        var id = Guid.NewGuid();
        hook.BlockNext("resolve-before-locks", id, "retry_delivery");
        var pending = hook.ReachAsync("resolve-before-locks", id, "retry_delivery", CancellationToken.None);
        await hook.WaitUntilReachedAsync("resolve-before-locks", id, "retry_delivery");
        hook.Reset();
        await pending;
        Assert.Equal(0, hook.GetInvocationCount("resolve-before-locks", id, "retry_delivery"));
    }
}
