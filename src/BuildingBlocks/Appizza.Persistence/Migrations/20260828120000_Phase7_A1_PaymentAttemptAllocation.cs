using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Appizza.Persistence;

namespace Appizza.Persistence.Migrations;

[DbContext(typeof(AppizzaDbContext))]
[Migration("20260828120000_Phase7_A1_PaymentAttemptAllocation")]
public partial class Phase7A1PaymentAttemptAllocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("create table payments.payment_attempt_allocation (id uuid primary key, payment_attempt_id uuid not null references payments.payment_attempt(id) on delete restrict, payment_plan_allocation_id uuid not null references payments.payment_plan_allocation(id) on delete restrict, amount numeric(14,2) not null);");
        migrationBuilder.Sql("create unique index ix_payment_attempt_allocation_payment_attempt_id_payment_plan_allocation_id on payments.payment_attempt_allocation (payment_attempt_id, payment_plan_allocation_id);");
        migrationBuilder.Sql("create index ix_payment_attempt_allocation_payment_plan_allocation_id on payments.payment_attempt_allocation (payment_plan_allocation_id);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop table if exists payments.payment_attempt_allocation;");
    }
}
