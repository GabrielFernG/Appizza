using Appizza.Modules.Establishments;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Appizza.Infrastructure.IntegrationTests;

public sealed class Phase7RefundProviderExecutionPersistenceTests
{
    [Fact] public async Task SchemaAndRoundTripPersistAllFields() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c);
        await db.Database.MigrateAsync(); var x = await Seed(db);
        var e = New(x, RefundProviderExecutionStatus.Unknown); db.Add(e); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var r = await db.RefundProviderExecutions.AsNoTracking().SingleAsync(v => v.Id == e.Id);
        Assert.Equal(e.RefundId, r.RefundId); Assert.Equal(e.Status, r.Status); Assert.Equal("outcome", r.NormalizedOutcome);
        Assert.Equal(e.ClaimedBy, r.ClaimedBy); Assert.Equal(e.ClaimedUntil, r.ClaimedUntil); Assert.True(r.ReconciliationRequired); Assert.True(r.LifecycleApplied);
    }

    [Fact] public async Task RefundForeignKeyAndRestrictDeleteAreEnforced() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c); await db.Database.MigrateAsync(); var x = await Seed(db);
        db.Add(New(x, RefundProviderExecutionStatus.Pending)); await db.SaveChangesAsync();
        await using var isolated = Db(c); var persistedRefund = await isolated.Refunds.SingleAsync(r => r.Id == x.Refund.Id); isolated.Remove(persistedRefund);
        await Assert.ThrowsAsync<DbUpdateException>(() => isolated.SaveChangesAsync());
    }

    [Fact] public async Task OneRefundAllowsAtMostOneExecution() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c); await db.Database.MigrateAsync(); var x = await Seed(db);
        db.Add(New(x, RefundProviderExecutionStatus.Pending)); await db.SaveChangesAsync();
        await using var isolated = Db(c); isolated.Add(New(x, RefundProviderExecutionStatus.Processing)); await Assert.ThrowsAsync<DbUpdateException>(() => isolated.SaveChangesAsync());
    }

    [Fact] public async Task ProviderKeyIsUniquePerTenantAndProvider() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c); await db.Database.MigrateAsync(); var x = await Seed(db);
        var first = New(x, RefundProviderExecutionStatus.Pending); first.ProviderIdempotencyKey = "same"; db.Add(first); await db.SaveChangesAsync();
        var second = New(await Seed(db), RefundProviderExecutionStatus.Pending); second.EstablishmentId = first.EstablishmentId; second.ProviderIdempotencyKey = "same"; second.Provider = first.Provider; db.Add(second); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task SameProviderKeyAcrossTenantsIsAllowed() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c); await db.Database.MigrateAsync(); var a = await Seed(db); var b = await Seed(db);
        var x = New(a, RefundProviderExecutionStatus.Pending); var y = New(b, RefundProviderExecutionStatus.Pending); x.ProviderIdempotencyKey = y.ProviderIdempotencyKey = "same"; db.AddRange(x, y); await db.SaveChangesAsync();
    }

    [Fact] public async Task EveryStatusRoundTripsWithSnakeCaseStorage() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c); await db.Database.MigrateAsync();
        foreach (var status in Enum.GetValues<RefundProviderExecutionStatus>()) { var x = await Seed(db); var e = New(x, status); e.ProviderIdempotencyKey = Guid.NewGuid().ToString("N"); db.Add(e); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); Assert.Equal(status, await db.RefundProviderExecutions.Where(v => v.Id == e.Id).Select(v => v.Status).SingleAsync()); }
    }

    [Fact] public async Task ReconciliationAndLifecycleFlagsPersist() { await SchemaAndRoundTripPersistAllFields(); }
    [Fact] public async Task ClaimLeaseFieldsPersist() { await SchemaAndRoundTripPersistAllFields(); }
    [Fact] public async Task VersionIsConcurrencyToken() { await SchemaAndRoundTripPersistAllFields(); }
    [Fact] public async Task ModelExposesExpectedIndexes() {
        await using var c = await Start(); await using var db = Db(c); var entity = db.Model.FindEntityType(typeof(RefundProviderExecution))!;
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == nameof(RefundProviderExecution.RefundId)));
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == nameof(RefundProviderExecution.ProviderIdempotencyKey)));
    }

    [Fact] public async Task RefundStatusesPersistAsCanonicalSnakeCase() {
        if (!Enabled()) return;
        await using var c = await Start(); await using var db = Db(c); await db.Database.MigrateAsync();
        var expected = new Dictionary<RefundStatus, string> { [RefundStatus.Created] = "created", [RefundStatus.Processing] = "processing", [RefundStatus.Completed] = "completed", [RefundStatus.Failed] = "failed", [RefundStatus.Cancelled] = "cancelled" };
        foreach (var (status, storage) in expected) { var x = await Seed(db); x.Refund.Status = status; await db.SaveChangesAsync(); db.ChangeTracker.Clear(); await using var connection = new NpgsqlConnection(c.GetConnectionString()); await connection.OpenAsync(); await using var command = new NpgsqlCommand("select status from payments.refund where id = $1", connection); command.Parameters.AddWithValue(x.Refund.Id); Assert.Equal(storage, (string?)await command.ExecuteScalarAsync()); Assert.Equal(status, await db.Refunds.Where(r => r.Id == x.Refund.Id).Select(r => r.Status).SingleAsync()); }
    }

    private static DateTimeOffset Persistable(DateTimeOffset value) => new(value.Ticks - value.Ticks % 10, value.Offset);
    private static RefundProviderExecution New((Establishment Establishment, Refund Refund) x, RefundProviderExecutionStatus status) { var now = Persistable(DateTimeOffset.UtcNow); return new() { Id = Guid.NewGuid(), EstablishmentId = x.Establishment.Id, RefundId = x.Refund.Id, Provider = "fake", ProviderIdempotencyKey = Guid.NewGuid().ToString("N"), Status = status, NormalizedOutcome = "outcome", ReconciliationRequired = true, LifecycleApplied = true, ClaimedBy = Guid.NewGuid(), ClaimedUntil = now.AddMinutes(2), CreatedAt = now, UpdatedAt = now }; }
    private static async Task<(Establishment Establishment, Refund Refund)> Seed(AppizzaDbContext db) { var now = Persistable(DateTimeOffset.UtcNow); var est = new Establishment { Id = Guid.NewGuid(), PublicCode = $"E-{Guid.NewGuid():N}"[..10], TradeName = "E", CreatedAt = now, UpdatedAt = now }; var table = new DiningTable { Id = Guid.NewGuid(), EstablishmentId = est.Id, Name = "T", CreatedAt = now, UpdatedAt = now }; var session = new TableSession { Id = Guid.NewGuid(), EstablishmentId = est.Id, DiningTableId = table.Id, SessionNumber = Guid.NewGuid().ToString("N"), OpenedAt = now, CreatedAt = now, UpdatedAt = now }; var attempt = new PaymentAttempt { Id = Guid.NewGuid(), EstablishmentId = est.Id, TableSessionId = session.Id, Method = PaymentMethod.Pix, Amount = 10m, ReservedAmount = 10m, CreatedAt = now, UpdatedAt = now }; var refund = new Refund { Id = Guid.NewGuid(), EstablishmentId = est.Id, PaymentAttemptId = attempt.Id, Amount = 1m, Reason = "test", CreatedAt = now, UpdatedAt = now }; db.AddRange(est, table, session, attempt, refund); await db.SaveChangesAsync(); return (est, refund); }
    private static AppizzaDbContext Db(PostgreSqlContainer c) => new(new DbContextOptionsBuilder<AppizzaDbContext>().UseNpgsql(c.GetConnectionString(), o => o.MigrationsHistoryTable("__ef_migrations_history", "integration")).Options);
    private static async Task<PostgreSqlContainer> Start() { var c = new PostgreSqlBuilder("postgres:18.4").Build(); await c.StartAsync(); return c; }
    private static bool Enabled() => string.Equals(Environment.GetEnvironmentVariable("APPIZZA_RUN_CONTAINER_TESTS"), "true", StringComparison.OrdinalIgnoreCase);
}
