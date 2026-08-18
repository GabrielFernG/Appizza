using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1707, CA1861

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_OrderingRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_order_status",
                schema: "ordering",
                table: "customer_order");

            migrationBuilder.CreateTable(
                name: "order_item_request",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_configuration = table.Column<string>(type: "jsonb", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    customer_note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    required_approval_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    price_difference = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    request_version = table.Column<int>(type: "integer", nullable: false),
                    original_revision_number = table.Column<int>(type: "integer", nullable: false),
                    requested_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    production_action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_request", x => x.id);
                    table.CheckConstraint("ck_order_item_request_cancellation_shape", "request_type <> 'cancel' or (requested_configuration is null and price_difference = 0)");
                    table.CheckConstraint("ck_order_item_request_production_action", "production_action is null or production_action in ('continue','restart','reject')");
                    table.CheckConstraint("ck_order_item_request_status", "status in ('pending_validation','pending_customer_confirmation','pending_operational_decision','approved','rejected','withdrawn','expired')");
                    table.CheckConstraint("ck_order_item_request_type", "request_type in ('cancel','change')");
                    table.ForeignKey(
                        name: "FK_order_item_request_customer_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "customer_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_item_request_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_item_request_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    new_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    substatus_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    customer_message = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_status_history_customer_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "customer_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item",
                sql: "status in ('awaiting_acceptance','accepted','awaiting_preparation','in_preparation','paused','ready','cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_order_status",
                schema: "ordering",
                table: "customer_order",
                sql: "status in ('submitted','partially_cancelled','cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_request_establishment_id_status_requested_at",
                schema: "ordering",
                table: "order_item_request",
                columns: new[] { "establishment_id", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_request_order_id",
                schema: "ordering",
                table: "order_item_request",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_request_order_item_id",
                schema: "ordering",
                table: "order_item_request",
                column: "order_item_id",
                unique: true,
                filter: "status in ('pending_validation','pending_customer_confirmation','pending_operational_decision')");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_history_order_id_changed_at",
                schema: "ordering",
                table: "order_status_history",
                columns: new[] { "order_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_request",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_status_history",
                schema: "ordering");

            migrationBuilder.DropCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_order_status",
                schema: "ordering",
                table: "customer_order");

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item",
                sql: "status in ('awaiting_acceptance','accepted','awaiting_preparation','in_preparation','paused','ready')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_order_status",
                schema: "ordering",
                table: "customer_order",
                sql: "status in ('submitted')");
        }
    }
}
