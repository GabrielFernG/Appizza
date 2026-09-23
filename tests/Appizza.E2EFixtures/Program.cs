using System.Text.Json;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var connection = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("ConnectionStrings__Appizza") ?? throw new InvalidOperationException("A PostgreSQL connection string is required.");
var options = new DbContextOptionsBuilder<AppizzaDbContext>().UseNpgsql(connection, n => n.MigrationsHistoryTable("__ef_migrations_history", "integration")).Options;
await using var db = new AppizzaDbContext(options);
await db.Database.MigrateAsync();
var now = DateTimeOffset.UtcNow;
var establishmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
if (!await db.Establishments.AnyAsync(x => x.Id == establishmentId)) db.Add(new Establishment { Id = establishmentId, PublicCode = "APPIZZA-DEV", TradeName = "Appizza E2E", Timezone = "America/Sao_Paulo", CurrencyCode = "BRL", CreatedAt = now, UpdatedAt = now });

var scenarios = new Dictionary<string, object>();
foreach (var name in new[] { "cash", "providerSuccess", "unknown", "failed", "multiplePartial", "permission" })
{
    var table = new DiningTable { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Name = $"E2E {name}", CreatedAt = now, UpdatedAt = now };
    var session = new TableSession { Id = Guid.NewGuid(), EstablishmentId = establishmentId, DiningTableId = table.Id, SessionNumber = $"E2E-{name}-{Guid.NewGuid():N}", Status = name == "cash" ? "closed" : "open", OpenedAt = now, ClosedAt = name == "cash" ? now : null, CreatedAt = now, UpdatedAt = now };
    var method = name == "cash" ? PaymentMethod.Cash : name == "failed" ? PaymentMethod.Debit : PaymentMethod.Pix;
    var attempt = new PaymentAttempt { Id = Guid.NewGuid(), EstablishmentId = establishmentId, TableSessionId = session.Id, Method = method, Status = PaymentAttemptStatus.Approved, Amount = 100m, ReservedAmount = 0m, Provider = method == PaymentMethod.Cash ? null : name, ProviderReference = method == PaymentMethod.Cash ? null : $"e2e-{name}", ApprovedAt = now, CreatedAt = now, UpdatedAt = now };
    db.AddRange(table, session, attempt);
    if (name == "multiplePartial")
    {
        var completed = new Refund { Id = Guid.NewGuid(), EstablishmentId = establishmentId, PaymentAttemptId = attempt.Id, Amount = 20m, Reason = "seed completed", Status = RefundStatus.Completed, CreatedAt = now, UpdatedAt = now };
        var processing = new Refund { Id = Guid.NewGuid(), EstablishmentId = establishmentId, PaymentAttemptId = attempt.Id, Amount = 15m, Reason = "seed processing", Status = RefundStatus.Processing, CreatedAt = now, UpdatedAt = now };
        var execution = new RefundProviderExecution { Id = Guid.NewGuid(), EstablishmentId = establishmentId, RefundId = processing.Id, Provider = "multiplePartial", ProviderIdempotencyKey = $"e2e-multiplePartial-{processing.Id:N}", Status = RefundProviderExecutionStatus.Processing, CreatedAt = now, UpdatedAt = now };
        db.AddRange(completed, processing, execution);
    }
    else if (name == "unknown")
    {
        var refund = new Refund { Id = Guid.NewGuid(), EstablishmentId = establishmentId, PaymentAttemptId = attempt.Id, Amount = 1m, Reason = "seed unknown", Status = RefundStatus.Processing, CreatedAt = now, UpdatedAt = now };
        db.Add(refund);
        db.Add(new RefundProviderExecution { Id = Guid.NewGuid(), EstablishmentId = establishmentId, RefundId = refund.Id, Provider = name, ProviderIdempotencyKey = $"e2e-{name}-{refund.Id:N}", Status = RefundProviderExecutionStatus.Unknown, ReconciliationRequired = true, CreatedAt = now, UpdatedAt = now });
    }
    else if (name == "failed")
    {
        db.Add(new Refund { Id = Guid.NewGuid(), EstablishmentId = establishmentId, PaymentAttemptId = attempt.Id, Amount = 1m, Reason = "seed failed", Status = RefundStatus.Failed, CreatedAt = now, UpdatedAt = now });
    }
    scenarios[name] = new { sessionId = session.Id, paymentAttemptId = attempt.Id };
}
var noRefundLogin = $"e2e-no-refund-{Guid.NewGuid():N}"[..24];
var noRefundUser = new User { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Name = "E2E sem refund", Login = noRefundLogin, Status = "active", CreatedAt = now, UpdatedAt = now };
noRefundUser.PasswordHash = new PasswordHasher<User>().HashPassword(noRefundUser, "E2ePassword!123");
var viewerRole = new Role { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Name = "E2E Viewer", IsSystemRole = false, CreatedAt = now, UpdatedAt = now };
var viewerPermissions = new List<Permission>
{
    new() { Id = Guid.NewGuid(), Code = "closing.view", Module = "closing", Name = "closing.view" },
    new() { Id = Guid.NewGuid(), Code = "payments.view", Module = "payments", Name = "payments.view" }
};
db.AddRange(noRefundUser, viewerRole);
db.AddRange(viewerPermissions);
db.AddRange(viewerPermissions.Select(permission => new RolePermission { Id = Guid.NewGuid(), RoleId = viewerRole.Id, PermissionId = permission.Id, CreatedAt = now }));
db.Add(new UserRole { Id = Guid.NewGuid(), UserId = noRefundUser.Id, RoleId = viewerRole.Id, CreatedAt = now });
await db.SaveChangesAsync();
await db.Database.ExecuteSqlRawAsync("create schema if not exists e2e_control");
await db.Database.ExecuteSqlRawAsync("create table if not exists e2e_control.refund_provider_scenario (provider text primary key, refund_outcome text not null, lookup_outcome text not null, released boolean not null default true)");
await db.Database.ExecuteSqlRawAsync("alter table e2e_control.refund_provider_scenario add column if not exists released boolean not null default true");
await db.Database.ExecuteSqlInterpolatedAsync($"insert into e2e_control.refund_provider_scenario(provider, refund_outcome, lookup_outcome, released) values ('providerSuccess','completed','completed',true), ('unknown','unknown','completed',false), ('failed','failed','failed',true), ('multiplePartial','completed','completed',false) on conflict (provider) do update set refund_outcome = excluded.refund_outcome, lookup_outcome = excluded.lookup_outcome, released = excluded.released");
Console.WriteLine(JsonSerializer.Serialize(new { establishmentId, scenarios, noRefundLogin, noRefundPassword = "E2ePassword!123" }));
