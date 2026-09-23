namespace Appizza.Table.Core;

public sealed class PaymentAttemptActionState
{
    public Guid? CurrentKey { get; private set; }
    public Guid BeginOrReuse() => CurrentKey ??= Guid.NewGuid();
    public void CompleteIfTerminal(string? status) { if (status is "Approved" or "Declined") CurrentKey = null; }
}
