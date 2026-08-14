using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    #pragma warning disable CA1707, CA1861
    /// <inheritdoc />
    public partial class Phase5_KitchenProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_production_item_phase4_status",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.AddColumn<int>(
                name: "current_attempt_number",
                schema: "kitchen",
                table: "production_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "preparation_started_at",
                schema: "kitchen",
                table: "production_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ready_at",
                schema: "kitchen",
                table: "production_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "production_attempt",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    failure_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_attempt", x => x.id);
                    table.CheckConstraint("ck_production_attempt_number", "attempt_number > 0");
                    table.CheckConstraint("ck_production_attempt_status", "status in ('active','completed','failed','abandoned')");
                    table.ForeignKey(
                        name: "FK_production_attempt_production_item_production_item_id",
                        column: x => x.production_item_id,
                        principalSchema: "kitchen",
                        principalTable: "production_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_pause",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paused_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resumed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pause", x => x.id);
                    table.ForeignKey(
                        name: "FK_production_pause_production_attempt_production_attempt_id",
                        column: x => x.production_attempt_id,
                        principalSchema: "kitchen",
                        principalTable: "production_attempt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_pause_production_item_production_item_id",
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
                sql: "status in ('awaiting_acceptance','accepted','awaiting_preparation','in_preparation','paused','ready')");

            migrationBuilder.CreateIndex(
                name: "ix_production_attempt_production_item_id",
                schema: "kitchen",
                table: "production_attempt",
                column: "production_item_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_production_attempt_production_item_id_attempt_number",
                schema: "kitchen",
                table: "production_attempt",
                columns: new[] { "production_item_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_pause_production_attempt_id",
                schema: "kitchen",
                table: "production_pause",
                column: "production_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_pause_production_item_id",
                schema: "kitchen",
                table: "production_pause",
                column: "production_item_id",
                unique: true,
                filter: "resumed_at is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_pause",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "production_attempt",
                schema: "kitchen");

            migrationBuilder.DropCheckConstraint(
                name: "ck_production_item_status",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.DropColumn(
                name: "current_attempt_number",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.DropColumn(
                name: "preparation_started_at",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.DropColumn(
                name: "ready_at",
                schema: "kitchen",
                table: "production_item");

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_item_phase4_status",
                schema: "kitchen",
                table: "production_item",
                sql: "status in ('awaiting_acceptance','accepted','awaiting_preparation')");
        }
    }
}
