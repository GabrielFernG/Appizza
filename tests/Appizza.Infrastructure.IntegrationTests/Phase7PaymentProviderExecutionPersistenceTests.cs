using Appizza.Modules.Establishments;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Appizza.Infrastructure.IntegrationTests;

public sealed class Phase7PaymentProviderExecutionPersistenceTests
{
    [Fact]
    public async Task PaymentProviderExecutionPersistsAndReloadsAllStatusesAndCanonicalValues()
    {
        if (!ContainersEnabled()) return;
        await using var postgres = await StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using var db = new AppizzaDbContext(options);
        await db.Database.MigrateAsync();
        var (establishment, attempt) = await SeedAsync(db);
        var values = new Dictionary<PaymentProviderExecutionStatus, string>
        {
            [PaymentProviderExecutionStatus.Pending] = "pending",
            [PaymentProviderExecutionStatus.Processing] = "processing",
            [PaymentProviderExecutionStatus.AwaitingCustomerAction] = "awaiting_customer_action",
            [PaymentProviderExecutionStatus.Unknown] = "unknown",
            [PaymentProviderExecutionStatus.OutcomeObserved] = "outcome_observed",
            [PaymentProviderExecutionStatus.Completed] = "completed",
            [PaymentProviderExecutionStatus.TerminalFailure] = "terminal_failure"
        };
        foreach (var (status, storage) in values)
        {
            var execution = new PaymentProviderExecution
            {
                Id = Guid.NewGuid(), EstablishmentId = establishment.Id, PaymentAttemptId = attempt.Id,
                Provider = "fake", ProviderIdempotencyKey = $"attempt:{attempt.Id:N}", Status = status,
                ProviderReference = status == PaymentProviderExecutionStatus.Unknown ? null : $"ref-{status}",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Add(execution);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var reloaded = await db.PaymentProviderExecutions.AsNoTracking().SingleAsync(x => x.Id == execution.Id);
            Assert.Equal(status, reloaded.Status);
            await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("select status from payments.payment_provider_execution where id = $1", connection);
            command.Parameters.AddWithValue(execution.Id);
            Assert.Equal(storage, (string?)await command.ExecuteScalarAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"delete from payments.payment_provider_execution where id = {execution.Id}");
        }
    }

    [Fact]
    public async Task PaymentProviderExecutionProtectsLogicalIdentityAndForeignKey()
    {
        if (!ContainersEnabled()) return;
        await using var postgres = await StartAsync();
        var options = Options(postgres.GetConnectionString());
        await using var db = new AppizzaDbContext(options);
        await db.Database.MigrateAsync();
        var (establishment, attempt) = await SeedAsync(db);
        db.Add(new PaymentProviderExecution { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, PaymentAttemptId = attempt.Id, Provider = "fake", ProviderIdempotencyKey = "stable", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.Add(new PaymentProviderExecution { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, PaymentAttemptId = attempt.Id, Provider = "fake", ProviderIdempotencyKey = "stable-2", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into payments.payment_provider_execution (id, establishment_id, payment_attempt_id, provider, provider_idempotency_key, status, lifecycle_applied, reconciliation_required, attempt_count, created_at, updated_at, version) values (gen_random_uuid(), $1, gen_random_uuid(), 'fake', 'orphan', 'pending', false, false, 0, now(), now(), 1)", connection);
        command.Parameters.AddWithValue(establishment.Id);
        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    private static DbContextOptions<AppizzaDbContext> Options(string connection) => new DbContextOptionsBuilder<AppizzaDbContext>().UseNpgsql(connection, o => o.MigrationsHistoryTable("__ef_migrations_history", "integration")).Options;

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18.4").Build();
        if (string.Equals(Environment.GetEnvironmentVariable("APPIZZA_RUN_CONTAINER_TESTS"), "true", StringComparison.OrdinalIgnoreCase)) await postgres.StartAsync();
        return postgres;
    }

    private static bool ContainersEnabled() => string.Equals(Environment.GetEnvironmentVariable("APPIZZA_RUN_CONTAINER_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    private static async Task<(Establishment Establishment, PaymentAttempt Attempt)> SeedAsync(AppizzaDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var establishment = new Establishment { Id = Guid.NewGuid(), PublicCode = $"B1-{Guid.NewGuid():N}"[..20], TradeName = "B1", CreatedAt = now, UpdatedAt = now };
        var table = new DiningTable { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, Name = "Mesa B1", CreatedAt = now, UpdatedAt = now };
        var session = new TableSession { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, DiningTableId = table.Id, SessionNumber = Guid.NewGuid().ToString("N"), OpenedAt = now, CreatedAt = now, UpdatedAt = now };
        var attempt = new PaymentAttempt { Id = Guid.NewGuid(), EstablishmentId = establishment.Id, TableSessionId = session.Id, Method = PaymentMethod.Pix, Amount = 10m, ReservedAmount = 10m, CreatedAt = now, UpdatedAt = now };
        db.AddRange(establishment, table, session, attempt);
        await db.SaveChangesAsync();
        return (establishment, attempt);
    }
}
