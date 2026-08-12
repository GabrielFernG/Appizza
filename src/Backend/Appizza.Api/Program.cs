using Appizza.Api;
using Appizza.BuildingBlocks;
using Appizza.Modules.Auditing;
using Appizza.Modules.Catalog;
using Appizza.Modules.Communications;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Appizza.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd("errorCode", "UNEXPECTED_ERROR");
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddOpenApi();
var phase1Security = builder.Configuration.GetSection(Phase1SecurityOptions.SectionName).Get<Phase1SecurityOptions>()
    ?? throw new InvalidOperationException("Phase1Security configuration must be provided.");
phase1Security.Validate();
var tokenService = new Phase1TokenService(phase1Security);
builder.Services.AddSingleton(phase1Security);
builder.Services.AddSingleton(tokenService);
builder.Services.AddSingleton<CpfProtector>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = tokenService.ValidationParameters();
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs")) context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("authentication", limiter =>
{
    limiter.PermitLimit = builder.Configuration.GetValue("AuthenticationRateLimit:PermitLimit", 10);
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
}));
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

if (app.Environment.IsDevelopment())
{
    await Phase1DevelopmentSeeder.SeedAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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
app.MapPhase1Endpoints();
app.MapPhase2Endpoints();
app.MapHub<Phase1Hub>("/hubs/v1/updates");

app.Run();

public partial class Program;
