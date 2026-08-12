using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Appizza.Api;
using Appizza.Modules.Devices;
using Appizza.Modules.Catalog;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Appizza.Api.IntegrationTests;

#pragma warning disable CA1001 // xUnit owns the asynchronous fixture lifetime.
#pragma warning disable CA1711 // The suffix communicates the xUnit collection role.
#pragma warning disable CA1822 // Helpers remain on the fixture for a cohesive test API.

[CollectionDefinition(Name)]
public sealed class Phase1ApiCollection : ICollectionFixture<Phase1ApiFixture>
{
    public const string Name = "Phase1 API PostgreSQL";
}

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase1ConcurrencyApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task TwoTabletsBindConcurrentlyWhenCapacityIsAvailable()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var first = await fixture.RegisterAsync();
        var second = await fixture.RegisterAsync();

        var responses = await fixture.ConcurrentAsync(
            () => fixture.BindAsync(tenant.AccessToken, first, tenant.TableIds[0]),
            () => fixture.BindAsync(tenant.AccessToken, second, tenant.TableIds[0]));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in responses)
        {
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await fixture.GetAsync("api/v1/table-devices/me", payload.RootElement.GetProperty("deviceAccessToken").GetString()!)).StatusCode);
        }
        await using var db = fixture.CreateDbContext();
        var bindings = await db.Set<DeviceTableBinding>().Where(x => x.DiningTableId == tenant.TableIds[0] && x.UnboundAt == null).ToListAsync();
        Assert.Equal(2, bindings.Count);
        Assert.Equal(2, bindings.Select(x => x.DeviceId).Distinct().Count());
        Assert.All(await db.Set<Device>().Where(x => x.Id == first.DeviceId || x.Id == second.DeviceId).ToListAsync(), device => Assert.Equal("active", device.Status));
    }

    [Fact]
    public async Task ConcurrentBindingCannotExceedTableLimit()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var existing = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var first = await fixture.RegisterAsync();
        var second = await fixture.RegisterAsync();

        var responses = await fixture.ConcurrentAsync(
            () => fixture.BindAsync(tenant.AccessToken, first, tenant.TableIds[0]),
            () => fixture.BindAsync(tenant.AccessToken, second, tenant.TableIds[0]));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("TABLE_DEVICE_LIMIT_REACHED", await fixture.ErrorCodeAsync(rejected));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.Set<DeviceTableBinding>().CountAsync(x => x.DiningTableId == tenant.TableIds[0] && x.UnboundAt == null));
        Assert.Contains(existing.DeviceId, await db.Set<DeviceTableBinding>().Where(x => x.DiningTableId == tenant.TableIds[0] && x.UnboundAt == null).Select(x => x.DeviceId).ToListAsync());
    }

    [Fact]
    public async Task SameDeviceCannotBindToTwoTablesConcurrently()
    {
        var tenant = await fixture.CreateTenantAsync(2, 2);
        var device = await fixture.RegisterAsync();

        var responses = await fixture.ConcurrentAsync(
            () => fixture.BindAsync(tenant.AccessToken, device, tenant.TableIds[0]),
            () => fixture.BindAsync(tenant.AccessToken, device, tenant.TableIds[1]));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("DEVICE_ALREADY_BOUND", await fixture.ErrorCodeAsync(rejected));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<DeviceTableBinding>().CountAsync(x => x.DeviceId == device.DeviceId && x.UnboundAt == null));
    }

    [Fact]
    public async Task ConcurrentReplacementLeavesConsistentBindingsAndRevokesOldCredentials()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var old = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        _ = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var first = await fixture.RegisterAsync();
        var second = await fixture.RegisterAsync();

        var responses = await fixture.ConcurrentAsync(
            () => fixture.BindAsync(tenant.AccessToken, first, tenant.TableIds[0], old.DeviceId),
            () => fixture.BindAsync(tenant.AccessToken, second, tenant.TableIds[0], old.DeviceId));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("DEVICE_ALREADY_BOUND", await fixture.ErrorCodeAsync(rejected));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.Set<DeviceTableBinding>().CountAsync(x => x.DiningTableId == tenant.TableIds[0] && x.UnboundAt == null));
        Assert.False(await db.Set<DeviceTableBinding>().AnyAsync(x => x.DeviceId == old.DeviceId && x.UnboundAt == null));
        Assert.Equal("revoked", await db.Set<Device>().Where(x => x.Id == old.DeviceId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.Set<OutboxMessage>().CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "device-replaced.v1"));
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.GetAsync("api/v1/table-devices/me", old.AccessToken)).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await fixture.RefreshDeviceAsync(old.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task RevokeAndBlockInvalidateActiveDeviceCredentialsWithoutSilentReactivation()
    {
        var tenant = await fixture.CreateTenantAsync(2, 2);
        var revoked = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        Assert.Equal(HttpStatusCode.OK, (await fixture.GetAsync("api/v1/table-devices/me", revoked.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.PostAsync($"api/v1/operations/table-devices/{revoked.DeviceId}/revoke-configuration", null, tenant.AccessToken)).StatusCode);
        Assert.Equal("DEVICE_CREDENTIAL_REVOKED", await fixture.ErrorCodeAsync(await fixture.GetAsync("api/v1/table-devices/me", revoked.AccessToken)));
        Assert.NotEqual(HttpStatusCode.OK, (await fixture.RefreshDeviceAsync(revoked.RefreshToken)).StatusCode);

        var blocked = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[1]);
        Assert.Equal(HttpStatusCode.OK, (await fixture.GetAsync("api/v1/table-devices/me", blocked.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.PostAsync($"api/v1/operations/table-devices/{blocked.DeviceId}/block", null, tenant.AccessToken)).StatusCode);
        Assert.Equal("DEVICE_BLOCKED", await fixture.ErrorCodeAsync(await fixture.GetAsync("api/v1/table-devices/me", blocked.AccessToken)));
        Assert.NotEqual(HttpStatusCode.OK, (await fixture.RefreshDeviceAsync(blocked.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.PostAsync($"api/v1/operations/table-devices/{blocked.DeviceId}/unblock", null, tenant.AccessToken)).StatusCode);
        Assert.Equal("DEVICE_CREDENTIAL_REVOKED", await fixture.ErrorCodeAsync(await fixture.GetAsync("api/v1/table-devices/me", blocked.AccessToken)));
    }

    [Fact]
    public async Task CrossTenantReadsAndWritesDoNotRevealOrMutateForeignResources()
    {
        var tenantA = await fixture.CreateTenantAsync(2, 1);
        var tenantB = await fixture.CreateTenantAsync(2, 1);
        var deviceA = await fixture.RegisterAndBindAsync(tenantA.AccessToken, tenantA.TableIds[0]);
        var deviceB = await fixture.RegisterAndBindAsync(tenantB.AccessToken, tenantB.TableIds[0]);
        var sessionB = await fixture.OpenSessionAsync(deviceB.AccessToken);
        await fixture.SetTableStatusAsync(tenantB.TableIds[0], "awaiting_cleaning");
        var foreignCandidate = await fixture.RegisterAsync();

        var available = await fixture.GetJsonAsync("api/v1/table-devices/configuration/available-tables", tenantA.AccessToken);
        Assert.DoesNotContain(tenantB.TableIds[0].ToString(), available.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var deviceView = await fixture.GetJsonAsync("api/v1/table-devices/me", deviceA.AccessToken);
        Assert.DoesNotContain(deviceB.DeviceId.ToString(), deviceView.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sessionB.ToString(), deviceView.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.BindAsync(tenantA.AccessToken, foreignCandidate, tenantB.TableIds[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.PostAsync($"api/v1/operations/table-devices/{deviceB.DeviceId}/revoke-configuration", null, tenantA.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.PostAsync($"api/v1/operations/tables/{tenantB.TableIds[0]}/confirm-cleaning", null, tenantA.AccessToken)).StatusCode);
        var identify = await fixture.PostAsync("api/v1/table-device/session/customer-identification", new { sessionId = sessionB, identificationType = "cpf", value = "52998224725", purposeAcknowledged = true }, deviceA.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, identify.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.Equal("active", await db.Set<Device>().Where(x => x.Id == deviceB.DeviceId).Select(x => x.Status).SingleAsync());
        Assert.Equal("awaiting_cleaning", await db.Set<DiningTable>().Where(x => x.Id == tenantB.TableIds[0]).Select(x => x.Status).SingleAsync());
        Assert.Equal("pending", await db.Set<TableSession>().Where(x => x.Id == sessionB).Select(x => x.CustomerIdentificationStatus).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentSameCpfIsIdempotentAndStoresOneIdentification()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var responses = await fixture.ConcurrentAsync(
            () => fixture.ProvideCpfAsync(context.Device.AccessToken, context.SessionId, "52998224725"),
            () => fixture.ProvideCpfAsync(context.Device.AccessToken, context.SessionId, "52998224725"));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await fixture.AssertIdentificationStateAsync(context.SessionId, "provided", 1);
    }

    [Fact]
    public async Task ConcurrentDifferentCpfsAllowExactlyOneWinner()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var responses = await fixture.ConcurrentAsync(
            () => fixture.ProvideCpfAsync(context.Device.AccessToken, context.SessionId, "52998224725"),
            () => fixture.ProvideCpfAsync(context.Device.AccessToken, context.SessionId, "16899535009"));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("CUSTOMER_IDENTIFICATION_ALREADY_RESOLVED", await fixture.ErrorCodeAsync(rejected));
        await fixture.AssertIdentificationStateAsync(context.SessionId, "provided", 1);
    }

    [Fact]
    public async Task ConcurrentProvideAndSkipProduceOneConsistentDecision()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var responses = await fixture.ConcurrentAsync(
            () => fixture.ProvideCpfAsync(context.Device.AccessToken, context.SessionId, "52998224725"),
            () => fixture.PostAsync("api/v1/table-device/session/customer-identification/skip", new { sessionId = context.SessionId }, context.Device.AccessToken));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var db = fixture.CreateDbContext();
        var status = await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.CustomerIdentificationStatus).SingleAsync();
        var count = await db.Set<SessionCustomerIdentification>().CountAsync(x => x.TableSessionId == context.SessionId);
        Assert.True((status == "provided" && count == 1) || (status == "skipped" && count == 0));
    }

    [Fact]
    public async Task UserAndDeviceRefreshTokensRotateOnlyOnceUnderConcurrency()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var userResponses = await fixture.ConcurrentAsync(
            () => fixture.RefreshUserAsync(tenant.RefreshToken),
            () => fixture.RefreshUserAsync(tenant.RefreshToken));
        Assert.Single(userResponses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(userResponses, response => response.StatusCode == HttpStatusCode.Unauthorized);

        var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var deviceResponses = await fixture.ConcurrentAsync(
            () => fixture.RefreshDeviceAsync(device.RefreshToken),
            () => fixture.RefreshDeviceAsync(device.RefreshToken));
        Assert.Single(deviceResponses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(deviceResponses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<UserSession>().CountAsync(x => x.UserId == tenant.UserId && x.RevokedAt == null));
        Assert.Equal(1, await db.Set<DeviceSession>().CountAsync(x => x.DeviceId == device.DeviceId && x.RevokedAt == null));
    }

    [Fact]
    public async Task ConcurrentOpenOrGetReturnsSameSessionAndSingleEventAndTransition()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var first = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var second = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var responses = await fixture.ConcurrentAsync(
            () => fixture.PostAsync("api/v1/table-device/session/open-or-get", null, first.AccessToken),
            () => fixture.PostAsync("api/v1/table-device/session/open-or-get", null, second.AccessToken));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ids.Add(json.RootElement.GetProperty("session").GetProperty("id").GetGuid());
        }
        Assert.Single(ids.Distinct());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<TableSession>().CountAsync(x => x.DiningTableId == tenant.TableIds[0]));
        Assert.Equal("occupied", await db.Set<DiningTable>().Where(x => x.Id == tenant.TableIds[0]).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.Set<OutboxMessage>().CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "table-session-opened.v1"));
        Assert.Equal(1, await db.Set<TableSessionStatusHistory>().CountAsync(x => x.TableSessionId == ids[0] && x.NewStatus == "open"));
    }
}

public sealed class Phase1ApiFixture : IAsyncLifetime
{
    private const string Password = "Development-test-password-42!";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4").Build();
    private Phase1TestFactory? _factory;
    private HttpClient? _client;
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        _factory = new Phase1TestFactory(ConnectionString);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public AppizzaDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppizzaDbContext>()
        .UseNpgsql(ConnectionString, options => options.MigrationsHistoryTable("__ef_migrations_history", "integration")).Options);

    public async Task<TenantContext> CreateTenantAsync(int deviceLimit, int tableCount)
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var establishment = new Establishment { Id = Guid.NewGuid(), PublicCode = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant(), TradeName = "Integration", CreatedAt = now, UpdatedAt = now };
        db.Add(establishment);
        db.AddRange(
            new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, SettingKey = Phase1SettingKeys.MaximumTableDevices, SettingValue = deviceLimit.ToString(System.Globalization.CultureInfo.InvariantCulture), ValueType = "integer", UpdatedAt = now },
            new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, SettingKey = Phase1SettingKeys.CpfRetentionDays, SettingValue = "30", ValueType = "integer", UpdatedAt = now });
        var tables = Enumerable.Range(1, tableCount).Select(index => new DiningTable { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, Name = $"Mesa {index}", InternalCode = $"M{index}", CreatedAt = now, UpdatedAt = now }).ToArray();
        db.AddRange(tables);
        var permissions = new List<Permission>();
        foreach (var code in Phase1Permissions.All.Concat(Phase2Permissions.All))
        {
            var permission = await db.Set<Permission>().SingleOrDefaultAsync(x => x.Code == code) ?? new Permission { Id = Guid.NewGuid(), Code = code, Module = code.Split('.')[0], Name = code };
            if (db.Entry(permission).State == EntityState.Detached) db.Add(permission);
            permissions.Add(permission);
        }
        var role = new Role { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, Name = "Administrator", IsSystemRole = true, CreatedAt = now, UpdatedAt = now };
        var user = new User { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, Name = "Admin", Login = "admin", CreatedAt = now, UpdatedAt = now };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        db.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, CreatedAt = now });
        db.AddRange(permissions.Select(permission => new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permission.Id, CreatedAt = now }));
        await db.SaveChangesAsync();
        var signIn = await PostAsync("api/v1/auth/sign-in", new { establishmentCode = establishment.PublicCode, login = "admin", password = Password });
        signIn.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await signIn.Content.ReadAsStringAsync());
        return new TenantContext(establishment.Id, user.Id, tables.Select(x => x.Id).ToArray(), json.RootElement.GetProperty("accessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    public async Task<RegisteredDevice> RegisterAsync()
    {
        var response = await PostAsync("api/v1/table-devices/register", new { installationId = Guid.NewGuid(), deviceName = "Test tablet", platform = "android", model = "test", operatingSystemVersion = "1", applicationVersion = "1.0" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new RegisteredDevice(json.RootElement.GetProperty("deviceId").GetGuid(), json.RootElement.GetProperty("configurationToken").GetString()!);
    }

    public async Task<BoundDevice> RegisterAndBindAsync(string userToken, Guid tableId)
    {
        var registered = await RegisterAsync();
        var response = await BindAsync(userToken, registered, tableId);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new BoundDevice(registered.DeviceId, json.RootElement.GetProperty("deviceAccessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    public Task<HttpResponseMessage> BindAsync(string userToken, RegisteredDevice device, Guid tableId, Guid? replaceDeviceId = null) =>
        PostAsync($"api/v1/table-devices/{device.DeviceId}/bind", new { tableId, configurationToken = device.ConfigurationToken, replaceDeviceId, reason = replaceDeviceId is null ? null : "test replacement" }, userToken, true);

    public Task<HttpResponseMessage> RefreshUserAsync(string refreshToken) => PostAsync("api/v1/auth/token/refresh", new { refreshToken });
    public Task<HttpResponseMessage> RefreshDeviceAsync(string refreshToken) => PostAsync("api/v1/table-devices/token/refresh", new { refreshToken });
    public Task<HttpResponseMessage> ProvideCpfAsync(string token, Guid sessionId, string cpf) => PostAsync("api/v1/table-device/session/customer-identification", new { sessionId, identificationType = "cpf", value = cpf, purposeAcknowledged = true }, token);

    public async Task<Guid> OpenSessionAsync(string deviceToken)
    {
        var response = await PostAsync("api/v1/table-device/session/open-or-get", null, deviceToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("session").GetProperty("id").GetGuid();
    }

    public async Task<OpenSessionContext> CreateOpenSessionAsync()
    {
        var tenant = await CreateTenantAsync(2, 1);
        var device = await RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        return new OpenSessionContext(device, await OpenSessionAsync(device.AccessToken));
    }

    public async Task AssertIdentificationStateAsync(Guid sessionId, string status, int count)
    {
        await using var db = CreateDbContext();
        Assert.Equal(status, await db.Set<TableSession>().Where(x => x.Id == sessionId).Select(x => x.CustomerIdentificationStatus).SingleAsync());
        Assert.Equal(count, await db.Set<SessionCustomerIdentification>().CountAsync(x => x.TableSessionId == sessionId));
    }

    public async Task SetTableStatusAsync(Guid tableId, string status)
    {
        await using var db = CreateDbContext();
        var table = await db.Set<DiningTable>().SingleAsync(x => x.Id == tableId);
        table.Status = status;
        await db.SaveChangesAsync();
    }

    public Task<HttpResponseMessage> GetAsync(string path, string token) => SendAsync(HttpMethod.Get, path, null, token, false);
    public async Task<HttpResponseMessage> GetConditionalAsync(string path, string token, string etag)
    { using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); request.Headers.TryAddWithoutValidation("If-None-Match", etag); return await _client!.SendAsync(request); }
    public async Task<JsonDocument> GetJsonAsync(string path, string token)
    {
        var response = await GetAsync(path, token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
    public Task<HttpResponseMessage> PostAsync(string path, object? body, string? token = null, bool idempotent = false) => SendAsync(HttpMethod.Post, path, body, token, idempotent);
    public Task<HttpResponseMessage> PutAsync(string path, object? body, string token) => SendAsync(HttpMethod.Put, path, body, token, false);
    public async Task<HttpResponseMessage> PutContentAsync(string path, byte[] content, string contentType, string token)
    { using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = new ByteArrayContent(content) }; request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return await _client!.SendAsync(request); }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? token, bool idempotent)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotent) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await _client!.SendAsync(request);
    }

    public async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    public async Task<HttpResponseMessage[]> ConcurrentAsync(params Func<Task<HttpResponseMessage>>[] operations)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operations.Select(async operation => { await gate.Task; return await operation(); }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    public sealed record TenantContext(Guid EstablishmentId, Guid UserId, Guid[] TableIds, string AccessToken, string RefreshToken);
    public sealed record RegisteredDevice(Guid DeviceId, string ConfigurationToken);
    public sealed record BoundDevice(Guid DeviceId, string AccessToken, string RefreshToken);
    public sealed record OpenSessionContext(BoundDevice Device, Guid SessionId);
}

internal sealed class Phase1TestFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var objectStorageEndpoint = Environment.GetEnvironmentVariable("APPIZZA_TEST_OBJECT_STORAGE_ENDPOINT") ?? "http://127.0.0.1:1";
        var objectStorageBucket = Environment.GetEnvironmentVariable("APPIZZA_TEST_OBJECT_STORAGE_BUCKET") ?? "test";
        var objectStorageAccessKey = Environment.GetEnvironmentVariable("APPIZZA_TEST_OBJECT_STORAGE_ACCESS_KEY") ?? "test";
        var objectStorageSecretKey = Environment.GetEnvironmentVariable("APPIZZA_TEST_OBJECT_STORAGE_SECRET_KEY") ?? "test";
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting("ConnectionStrings:Appizza", connectionString);
        builder.UseSetting("ObjectStorage:Endpoint", objectStorageEndpoint);
        builder.UseSetting("ObjectStorage:Bucket", objectStorageBucket);
        builder.UseSetting("ObjectStorage:AccessKey", objectStorageAccessKey);
        builder.UseSetting("ObjectStorage:SecretKey", objectStorageSecretKey);
        builder.UseSetting("ObjectStorage:UsePathStyle", "true");
        builder.UseSetting("Phase1Security:SigningKey", "integration-signing-key-with-at-least-32-bytes");
        builder.UseSetting("Phase1Security:CpfEncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        builder.UseSetting("Phase1Security:CpfHmacKey", "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=");
        builder.UseSetting("AuthenticationRateLimit:PermitLimit", "1000");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Appizza"] = connectionString,
            ["ObjectStorage:Endpoint"] = objectStorageEndpoint,
            ["ObjectStorage:Bucket"] = objectStorageBucket,
            ["ObjectStorage:AccessKey"] = objectStorageAccessKey,
            ["ObjectStorage:SecretKey"] = objectStorageSecretKey,
            ["ObjectStorage:UsePathStyle"] = "true",
            ["Phase1Security:SigningKey"] = "integration-signing-key-with-at-least-32-bytes",
            ["Phase1Security:CpfEncryptionKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
            ["Phase1Security:CpfHmacKey"] = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=",
            ["AuthenticationRateLimit:PermitLimit"] = "1000"
        }));
    }
}
