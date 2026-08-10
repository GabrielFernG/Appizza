using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Appizza.Persistence;

public sealed class AppizzaDbContextFactory : IDesignTimeDbContextFactory<AppizzaDbContext>
{
    public AppizzaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Appizza")
            ?? throw new InvalidOperationException("ConnectionStrings__Appizza must be configured for migrations.");

        var options = new DbContextOptionsBuilder<AppizzaDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "integration"))
            .Options;

        return new AppizzaDbContext(options);
    }
}
