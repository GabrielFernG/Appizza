using System.Collections.Concurrent;

namespace Appizza.Persistence;

public interface IPhase5DeliveryConcurrencyHook
{
    Task ReachAsync(string stage, Guid resourceId, CancellationToken cancellationToken);
    Task ReachAsync(string stage, Guid resourceId, string? operation, CancellationToken cancellationToken);
}

public sealed class Phase5DeliveryConcurrencyHook : IPhase5DeliveryConcurrencyHook
{
    private readonly ConcurrentDictionary<(string Stage, Guid Resource, string? Operation), Gate> _gates = new();
    public void BlockNext(string stage, Guid resourceId, string? operation = null) => _gates.GetOrAdd((stage, resourceId, operation), _ => new()).Blocked = true;
    public Task WaitUntilReachedAsync(string stage, Guid resourceId, string? operation = null) => _gates.GetOrAdd((stage, resourceId, operation), _ => new()).Reached.Task;
    public void Release(string stage, Guid resourceId, string? operation = null) { if (_gates.TryGetValue((stage, resourceId, operation), out var gate)) { gate.Blocked = false; gate.Release.TrySetResult(); } }
    public int GetInvocationCount(string stage, Guid resourceId, string? operation = null) => _gates.TryGetValue((stage, resourceId, operation), out var gate) ? gate.Count : 0;
    public void Reset()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Blocked = false;
            gate.Release.TrySetResult();
        }
        _gates.Clear();
    }
    public async Task ReachAsync(string stage, Guid resourceId, CancellationToken cancellationToken)
        => await ReachAsync(stage, resourceId, null, cancellationToken);
    public async Task ReachAsync(string stage, Guid resourceId, string? operation, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd((stage, resourceId, operation), _ => new()); Interlocked.Increment(ref gate.Count); gate.Reached.TrySetResult();
        if (gate.Blocked) await gate.Release.Task.WaitAsync(cancellationToken);
    }
    private sealed class Gate
    {
        public int Count; public bool Blocked;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
