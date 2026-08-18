using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1707, CA1861

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_OrderItemRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "customer_confirmed_at",
                schema: "ordering",
                table: "order_item_request",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "customer_confirmed_version",
                schema: "ordering",
                table: "order_item_request",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_hash",
                schema: "ordering",
                table: "order_item_request",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_snapshot",
                schema: "ordering",
                table: "order_item_request",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "current_revision_number",
                schema: "ordering",
                table: "order_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "order_item_revision",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    source_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot_schema_version = table.Column<int>(type: "integer", nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false),
                    previous_unit_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    unit_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    previous_total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    price_difference = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    catalog_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    availability_version = table.Column<long>(type: "bigint", nullable: false),
                    configuration_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_revision", x => x.id);
                    table.CheckConstraint("ck_order_item_revision_actor", "not (effective_by_user_id is not null and effective_by_device_id is not null)");
                    table.CheckConstraint("ck_order_item_revision_amounts", "previous_unit_amount >= 0 and unit_amount >= 0 and previous_total_amount >= 0 and total_amount >= 0");
                    table.CheckConstraint("ck_order_item_revision_number", "revision_number > 0");
                    table.ForeignKey(
                        name: "FK_order_item_revision_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_item_revision_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_item_revision_order_item_request_source_request_id",
                        column: x => x.source_request_id,
                        principalSchema: "ordering",
                        principalTable: "order_item_request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_revision_establishment_id_order_item_id_effectiv~",
                schema: "ordering",
                table: "order_item_revision",
                columns: new[] { "establishment_id", "order_item_id", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_revision_order_item_id_revision_number",
                schema: "ordering",
                table: "order_item_revision",
                columns: new[] { "order_item_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_revision_source_request_id",
                schema: "ordering",
                table: "order_item_revision",
                column: "source_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_revision",
                schema: "ordering");

            migrationBuilder.DropColumn(
                name: "customer_confirmed_at",
                schema: "ordering",
                table: "order_item_request");

            migrationBuilder.DropColumn(
                name: "customer_confirmed_version",
                schema: "ordering",
                table: "order_item_request");

            migrationBuilder.DropColumn(
                name: "review_hash",
                schema: "ordering",
                table: "order_item_request");

            migrationBuilder.DropColumn(
                name: "reviewed_snapshot",
                schema: "ordering",
                table: "order_item_request");

            migrationBuilder.DropColumn(
                name: "current_revision_number",
                schema: "ordering",
                table: "order_item");
        }
    }
}
