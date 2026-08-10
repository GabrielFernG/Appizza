using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Appizza.Infrastructure.IntegrationTests;

public sealed class PostgreSqlFoundationTests
{
    [Fact]
    public async Task FoundationMigrationCreatesOnlyTechnicalTables()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("APPIZZA_RUN_CONTAINER_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var postgres = new PostgreSqlBuilder("postgres:18.4").Build();
        await postgres.StartAsync(CancellationToken.None);

        var options = new DbContextOptionsBuilder<AppizzaDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "integration"))
            .Options;
        await using var dbContext = new AppizzaDbContext(options);
        await dbContext.Database.MigrateAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            """
            select table_schema, table_name
            from information_schema.tables
            where table_schema not in ('pg_catalog', 'information_schema')
            order by table_schema, table_name
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var tables = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            tables.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        Assert.Contains("integration.outbox_message", tables);
        Assert.Contains("integration.inbox_message", tables);
        Assert.Contains("integration.idempotency_record", tables);
        Assert.DoesNotContain(tables, table => table.StartsWith("catalog.", StringComparison.Ordinal));
    }
}
