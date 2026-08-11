using Appizza.Persistence;
using Appizza.Modules.Establishments;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Appizza.Infrastructure.IntegrationTests;

public sealed class PostgreSqlFoundationTests
{
    [Fact]
    public async Task AllMigrationsCreateExpectedTablesAndProtectConcurrentActiveSessions()
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
        Assert.Contains("establishments.establishment", tables);
        Assert.Contains("identity.user", tables);
        Assert.Contains("devices.device_session", tables);
        Assert.Contains("tables.table_session", tables);
        Assert.DoesNotContain(tables, table => table.StartsWith("catalog.", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.StartsWith("ordering.", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.StartsWith("payments.", StringComparison.Ordinal));

        var establishment = new Establishment
        {
            Id = Guid.NewGuid(), PublicCode = "CONCURRENCY", TradeName = "Concurrency",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var diningTable = new DiningTable
        {
            Id = Guid.NewGuid(), EstablishmentId = establishment.Id, Name = "Mesa 1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(establishment, diningTable);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        async Task<bool> TryOpenAsync(string number)
        {
            await using var competing = new AppizzaDbContext(options);
            competing.Add(new TableSession
            {
                Id = Guid.NewGuid(), EstablishmentId = establishment.Id,
                DiningTableId = diningTable.Id, SessionNumber = number,
                OpenedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            try
            {
                await competing.SaveChangesAsync(CancellationToken.None);
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var attempts = await Task.WhenAll(TryOpenAsync("20260811-000001"), TryOpenAsync("20260811-000002"));
        Assert.Single(attempts, result => result);
        Assert.Equal(1, await dbContext.Set<TableSession>().CountAsync(
            session => session.DiningTableId == diningTable.Id,
            CancellationToken.None));
    }
}
