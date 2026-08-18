using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1707, CA1861

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_Delivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.CreateTable(
                name: "delivery_confirmation",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmation_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_confirmation", x => x.id);
                    table.CheckConstraint("ck_delivery_confirmation_actor", "not (confirmed_by_user_id is not null and confirmed_by_device_id is not null)");
                    table.CheckConstraint("ck_delivery_confirmation_sequence", "sequence_number > 0");
                    table.CheckConstraint("ck_delivery_confirmation_status", "status in ('pending','confirmed_manual','confirmed_automatic','contested','superseded')");
                    table.ForeignKey(
                        name: "FK_delivery_confirmation_production_item_production_item_id",
                        column: x => x.production_item_id,
                        principalSchema: "kitchen",
                        principalTable: "production_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_contest",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_confirmation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolution = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_contest", x => x.id);
                    table.CheckConstraint("ck_delivery_contest_actor", "not (opened_by_user_id is not null and opened_by_device_id is not null)");
                    table.CheckConstraint("ck_delivery_contest_resolution", "(status = 'open' and resolution is null and resolved_at is null) or (status <> 'open' and resolution in ('confirm_delivered','retry_delivery') and resolved_at is not null)");
                    table.CheckConstraint("ck_delivery_contest_status", "status in ('open','resolved_delivered','resolved_retry')");
                    table.ForeignKey(
                        name: "FK_delivery_contest_delivery_confirmation_delivery_confirmatio~",
                        column: x => x.delivery_confirmation_id,
                        principalSchema: "kitchen",
                        principalTable: "delivery_confirmation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_delivery_contest_production_item_production_item_id",
                        column: x => x.production_item_id,
                        principalSchema: "kitchen",
                        principalTable: "production_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item",
                sql: "status in ('awaiting_acceptance','accepted','awaiting_preparation','in_preparation','paused','ready','awaiting_delivery_confirmation','delivered','cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_confirmation_establishment_id_production_item_id_s~",
                schema: "kitchen",
                table: "delivery_confirmation",
                columns: new[] { "establishment_id", "production_item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_confirmation_production_item_id",
                schema: "kitchen",
                table: "delivery_confirmation",
                column: "production_item_id",
                unique: true,
                filter: "status in ('pending','contested')");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_confirmation_production_item_id_sequence_number",
                schema: "kitchen",
                table: "delivery_confirmation",
                columns: new[] { "production_item_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_confirmation_status_expires_at",
                schema: "kitchen",
                table: "delivery_confirmation",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_contest_delivery_confirmation_id",
                schema: "kitchen",
                table: "delivery_contest",
                column: "delivery_confirmation_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_contest_production_item_id",
                schema: "kitchen",
                table: "delivery_contest",
                column: "production_item_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_contest_production_item_id_status",
                schema: "kitchen",
                table: "delivery_contest",
                columns: new[] { "production_item_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_contest",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "delivery_confirmation",
                schema: "kitchen");

            migrationBuilder.DropCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item",
                sql: "status in ('awaiting_acceptance','accepted','awaiting_preparation','in_preparation','paused','ready','cancelled')");
        }
    }
}
