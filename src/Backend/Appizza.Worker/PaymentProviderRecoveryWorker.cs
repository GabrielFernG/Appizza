using Appizza.Payments.Application;

namespace Appizza.Worker;

public sealed class PaymentProviderRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PaymentProviderRecoveryWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogCycleFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2101, "PaymentRecoveryCycleFailed"), "Payment provider recovery cycle failed.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchSize = Math.Max(1, configuration.GetValue("Payments:Worker:BatchSize", 25));
        var interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Payments:Worker:PollSeconds", 30)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var recovery = scope.ServiceProvider.GetRequiredService<PaymentProviderRecoveryService>();
                await recovery.ProcessBatchAsync(batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { LogCycleFailure(logger, ex); }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }
}
