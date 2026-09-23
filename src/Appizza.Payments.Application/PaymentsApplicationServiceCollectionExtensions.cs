using Microsoft.Extensions.DependencyInjection;

namespace Appizza.Payments.Application;

public static class PaymentsApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentsApplication(this IServiceCollection services)
    {
        services.AddScoped<PaymentAttemptLifecycleService>();
        services.AddScoped<PaymentProcessingService>();
        services.AddScoped<PaymentProviderRecoveryService>();
        services.AddScoped<RefundProviderRecoveryService>();
        return services;
    }
}
