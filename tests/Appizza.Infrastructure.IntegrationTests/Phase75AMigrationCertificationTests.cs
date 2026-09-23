using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Appizza.Infrastructure.IntegrationTests;

public sealed class Phase75AMigrationCertificationTests
{
    private static readonly string[] AllowedDeleteRules = ["RESTRICT", "NO ACTION"];
    [Fact]
    public async Task EmptyDatabaseMigratesThroughPhase75A()
    {
        if (!Enabled()) return;
        await using var container = await StartAsync();
        await using var db = CreateDb(container);
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.Equal(19, migrations.Length);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(19, applied.Length);
        Assert.Equal(migrations[^1], applied[^1]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await TableExistsAsync(container, "refund_provider_execution"));
    }

    [Fact]
    public async Task Migration18UpgradesToPhase75A()
    {
        if (!Enabled()) return;
        await using var container = await StartAsync();
        await using var db = CreateDb(container);
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.Equal(19, migrations.Length);
        await db.Database.MigrateAsync(migrations[17]);
        var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(18, before.Length);
        Assert.DoesNotContain(migrations[^1], before);
        await db.Database.MigrateAsync(migrations[^1]);
        var after = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(19, after.Length);
        Assert.Equal(migrations[^1], after[^1]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await TableExistsAsync(container, "refund_provider_execution"));
        await AssertSchemaAsync(container);
    }

    private static async Task AssertSchemaAsync(PostgreSqlContainer container)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from information_schema.columns where table_schema='payments' and table_name='refund_provider_execution'", connection);
        Assert.Equal(17L, (long)(await command.ExecuteScalarAsync())!);
        await using var fk = new NpgsqlCommand("select delete_rule from information_schema.referential_constraints where constraint_name='FK_refund_provider_execution_refund_refund_id'", connection);
        var deleteRule = (string?)await fk.ExecuteScalarAsync();
        Assert.Contains(deleteRule, AllowedDeleteRules);
        await using var indexes = new NpgsqlCommand("select count(*) from pg_indexes where schemaname='payments' and tablename='refund_provider_execution' and indexdef like '%UNIQUE%'", connection);
        Assert.True((long)(await indexes.ExecuteScalarAsync())! >= 2);
    }

    private static async Task<bool> TableExistsAsync(PostgreSqlContainer container, string table)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString()); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select exists (select 1 from information_schema.tables where table_schema='payments' and table_name=$1)", connection); command.Parameters.AddWithValue(table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static AppizzaDbContext CreateDb(PostgreSqlContainer container) => new(new DbContextOptionsBuilder<AppizzaDbContext>().UseNpgsql(container.GetConnectionString(), o => o.MigrationsHistoryTable("__ef_migrations_history", "integration")).Options);
    private static async Task<PostgreSqlContainer> StartAsync() { var container = new PostgreSqlBuilder("postgres:18.4").Build(); await container.StartAsync(); return container; }
    private static bool Enabled() => string.Equals(Environment.GetEnvironmentVariable("APPIZZA_RUN_CONTAINER_TESTS"), "true", StringComparison.OrdinalIgnoreCase);
}
