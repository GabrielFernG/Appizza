using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace Appizza.Api.IntegrationTests;

public sealed class FoundationApiTests : IClassFixture<FoundationApiFactory>
{
    private readonly HttpClient _client;

    public FoundationApiTests(FoundationApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LiveHealthDoesNotDependOnPostgresql()
    {
        var response = await _client.GetAsync("/health/live", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CorrelationIdIsPropagated()
    {
        var correlationId = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/foundation/modules");
        request.Headers.Add("X-Correlation-ID", correlationId);

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task AllModulesAreExposedByTheFoundationEndpoint()
    {
        var response = await _client.GetFromJsonAsync<ModuleResponse>(
            "/api/v1/foundation/modules",
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(15, response.Modules.Length);
        Assert.Contains("Media", response.Modules);
    }

    private sealed record ModuleResponse(string[] Modules);
}

public sealed class FoundationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting("ConnectionStrings:Appizza",
            "Host=127.0.0.1;Port=1;Database=appizza;Username=test;Password=test;Timeout=1");
        builder.UseSetting("ObjectStorage:Endpoint", "http://127.0.0.1:1");
        builder.UseSetting("ObjectStorage:Bucket", "test");
        builder.UseSetting("ObjectStorage:AccessKey", "test");
        builder.UseSetting("ObjectStorage:SecretKey", "test");
        builder.UseSetting("ObjectStorage:UsePathStyle", "true");
        builder.UseSetting("Phase1Security:SigningKey", "test-signing-key-with-at-least-32-bytes-long");
        builder.UseSetting("Phase1Security:CpfEncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        builder.UseSetting("Phase1Security:CpfHmacKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Appizza"] =
                    "Host=127.0.0.1;Port=1;Database=appizza;Username=test;Password=test;Timeout=1",
                ["ObjectStorage:Endpoint"] = "http://127.0.0.1:1",
                ["ObjectStorage:Bucket"] = "test",
                ["ObjectStorage:AccessKey"] = "test",
                ["ObjectStorage:SecretKey"] = "test",
                ["ObjectStorage:UsePathStyle"] = "true"
                ,["Phase1Security:SigningKey"] = "test-signing-key-with-at-least-32-bytes-long"
                ,["Phase1Security:CpfEncryptionKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
                ,["Phase1Security:CpfHmacKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
            });
        });
    }
}
