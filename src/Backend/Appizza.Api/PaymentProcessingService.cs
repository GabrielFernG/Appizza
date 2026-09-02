using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

/// <summary>Bridges provider results to the certified A2 lifecycle.</summary>
internal sealed class PaymentProcessingService(AppizzaDbContext db, IPaymentProvider provider, PaymentAttemptLifecycleService lifecycle)
{
    public async Task<PaymentProviderStatus> StartAsync(Guid tenant, Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct)
            ?? throw new InvalidOperationException("PAYMENT_ATTEMPT_NOT_FOUND");
        if (attempt.Method == PaymentMethod.Cash) throw new InvalidOperationException("CASH_DOES_NOT_USE_PROVIDER");
        var result = await provider.StartPaymentAsync(new StartPaymentRequest(attempt.Id, attempt.Amount, attempt.Method, attempt.ProviderReference ?? $"attempt:{attempt.Id:N}"), ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await db.Set<PaymentAttempt>().SingleAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct);
        current.Provider = provider.GetType().Name;
        current.ProviderReference = result.ProviderReference;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public Task ApplyResultAsync(Guid tenant, Guid attemptId, PaymentProviderStatus result, CancellationToken ct = default) =>
        result.Status.Trim().ToLowerInvariant() switch
        {
            "approved" => lifecycle.SucceedAsync(tenant, attemptId, ct),
            "declined" => lifecycle.FailAsync(tenant, attemptId, ct),
            "unknown" => lifecycle.MarkUnknownAsync(tenant, attemptId, ct),
            _ => Task.CompletedTask
        };
}
