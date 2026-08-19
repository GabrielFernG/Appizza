using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1707, CA1861

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6PromotionsCommunications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_order_amounts",
                schema: "ordering",
                table: "customer_order");

            migrationBuilder.EnsureSchema(
                name: "promotions");

            migrationBuilder.CreateTable(
                name: "promotion",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion", x => x.id);
                    table.CheckConstraint("ck_promotion_status", "status in ('draft','active','inactive','expired')");
                });

            migrationBuilder.CreateTable(
                name: "promotion_application",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eligible_base_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_application", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "promotion_version",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    value = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    eligible_product_ids = table.Column<string>(type: "jsonb", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_version", x => x.id);
                    table.CheckConstraint("ck_promotion_version_kind", "kind in ('percentage','fixed_amount')");
                    table.CheckConstraint("ck_promotion_version_scope", "scope in ('entire_order','specific_products')");
                    table.CheckConstraint("ck_promotion_version_value", "value >= 0");
                    table.ForeignKey(
                        name: "FK_promotion_version_promotion_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "promotions",
                        principalTable: "promotion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_order_amounts",
                schema: "ordering",
                table: "customer_order",
                sql: "subtotal_amount >= 0 and discount_amount >= 0 and total_amount >= 0 and total_amount = subtotal_amount - discount_amount");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_establishment_id_status",
                schema: "promotions",
                table: "promotion",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_application_establishment_id_promotion_id",
                schema: "promotions",
                table: "promotion_application",
                columns: new[] { "establishment_id", "promotion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_application_order_id",
                schema: "promotions",
                table: "promotion_application",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotion_version_establishment_id_starts_at_ends_at",
                schema: "promotions",
                table: "promotion_version",
                columns: new[] { "establishment_id", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_version_promotion_id",
                schema: "promotions",
                table: "promotion_version",
                column: "promotion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promotion_application",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "promotion_version",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "promotion",
                schema: "promotions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_order_amounts",
                schema: "ordering",
                table: "customer_order");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_order_amounts",
                schema: "ordering",
                table: "customer_order",
                sql: "subtotal_amount >= 0 and discount_amount = 0 and total_amount >= 0");
        }
    }
}
