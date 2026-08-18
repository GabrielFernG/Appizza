using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Appizza.Infrastructure.IntegrationTests;

public sealed class Phase5DeliveryModelTests
{
    [Fact]
    public async Task DeliverySchemaExposesPhysicalConstraintsIndexesAndSettingsKeys()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("APPIZZA_RUN_CONTAINER_TESTS"), "true", StringComparison.OrdinalIgnoreCase)) return;
        await using var postgres = new PostgreSqlBuilder("postgres:18.4").Build();
        await postgres.StartAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<AppizzaDbContext>().UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "integration")).Options;
        await using var db = new AppizzaDbContext(options);
        await db.Database.MigrateAsync(CancellationToken.None);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);

        Assert.Equal(5, await ScalarAsync(connection, "select count(*) from pg_constraint where conname in ('ck_delivery_confirmation_sequence','ck_delivery_confirmation_status','ck_delivery_contest_status','ck_delivery_contest_resolution','ck_production_item_status')"));
        Assert.Equal(2, await ScalarAsync(connection, "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='kitchen' and c.relname in ('delivery_confirmation','delivery_contest')"));
        Assert.True(await ScalarAsync(connection, "select count(*) from pg_indexes where schemaname='kitchen' and indexname like 'ix_delivery_%'") >= 6);
        Assert.Equal(4, await ScalarAsync(connection, "select count(*) from information_schema.columns where table_schema='kitchen' and table_name='delivery_confirmation' and column_name in ('sequence_number','status','expires_at','version')"));
        Assert.Equal(1, await ScalarAsync(connection, "select count(*) from information_schema.columns where table_schema='kitchen' and table_name='delivery_contest' and column_name='resolution'"));
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture);
    }
}
