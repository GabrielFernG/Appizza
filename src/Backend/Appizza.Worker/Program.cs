using Appizza.Persistence;
using Appizza.Worker;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Appizza")
    ?? throw new InvalidOperationException("ConnectionStrings:Appizza must be configured.");

builder.Services.AddDbContext<AppizzaDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", "integration")));
builder.Services.AddSingleton<Phase5DeliveryConcurrencyHook>();
builder.Services.AddSingleton<IPhase5DeliveryConcurrencyHook>(p => p.GetRequiredService<Phase5DeliveryConcurrencyHook>());
builder.Services.AddHostedService<OutboxMonitorWorker>();
builder.Services.AddHostedService<ExpiredCpfCleanupWorker>();
builder.Services.AddHostedService<DeliveryAutoConfirmationHostedWorker>();

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Appizza.Worker"))
    .WithMetrics(metrics =>
    {
        metrics.AddHttpClientInstrumentation().AddRuntimeInstrumentation();
        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
        }
    })
    .WithTracing(tracing =>
    {
        tracing.AddHttpClientInstrumentation();
        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
        }
    });

await builder.Build().RunAsync();
