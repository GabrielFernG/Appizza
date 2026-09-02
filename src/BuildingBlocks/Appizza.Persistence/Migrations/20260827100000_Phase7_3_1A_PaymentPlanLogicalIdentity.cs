using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Appizza.Persistence;

#nullable disable
#pragma warning disable CA1707, CA1861

namespace Appizza.Persistence.Migrations;

[DbContext(typeof(AppizzaDbContext))]
[Migration("20260827100000_Phase7_3_1A_PaymentPlanLogicalIdentity")]
public partial class Phase731APaymentPlanLogicalIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "logical_plan_id",
            schema: "payments",
            table: "payment_plan",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql("update payments.payment_plan set logical_plan_id = id where logical_plan_id is null;");

        migrationBuilder.AlterColumn<Guid>(
            name: "logical_plan_id",
            schema: "payments",
            table: "payment_plan",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_payment_plan_logical_version",
            schema: "payments",
            table: "payment_plan",
            columns: new[] { "establishment_id", "table_session_id", "logical_plan_id", "version" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_payment_plan_logical_version",
            schema: "payments",
            table: "payment_plan");

        migrationBuilder.DropColumn(
            name: "logical_plan_id",
            schema: "payments",
            table: "payment_plan");
    }
}
