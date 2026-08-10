using Appizza.Api;
using Appizza.BuildingBlocks;
using Appizza.Modules.Auditing;
using Appizza.Modules.Catalog;
using Appizza.Modules.Communications;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Integration;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Media;
using Appizza.Modules.Operations;
using Appizza.Modules.Ordering;
using Appizza.Modules.Payments;
using Appizza.Modules.Promotions;
using Appizza.Modules.Reporting;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["errorCode"] = "UNEXPECTED_ERROR";
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"])
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddAppizzaTelemetry(builder.Configuration, "Appizza.Api");

var connectionString = builder.Configuration.GetConnectionString("Appizza")
    ?? throw new InvalidOperationException("ConnectionStrings:Appizza must be configured.");
builder.Services.AddDbContext<AppizzaDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", "integration")));

var storageOptions = builder.Configuration
    .GetSection(ObjectStorageOptions.SectionName)
    .Get<ObjectStorageOptions>()
    ?? throw new InvalidOperationException("ObjectStorage configuration must be provided.");
builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();

IAppizzaModule[] modules =
[
    new EstablishmentsModule(), new IdentityModule(), new CatalogModule(), new OrderingModule(),
    new KitchenModule(), new TablesModule(), new PaymentsModule(), new PromotionsModule(),
    new MediaModule(), new CommunicationsModule(), new DevicesModule(), new OperationsModule(),
    new ReportingModule(), new AuditingModule(), new IntegrationModule()
];
builder.Services.AddSingleton<IReadOnlyCollection<IAppizzaModule>>(modules);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/v1/foundation/modules", (IReadOnlyCollection<IAppizzaModule> registeredModules) =>
    Results.Ok(new { modules = registeredModules.Select(module => module.Name).OrderBy(name => name) }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
