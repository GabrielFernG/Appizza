using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1707, CA1861 // EF-generated migration naming and inline column arrays.

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_Communications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communications");

            migrationBuilder.CreateTable(
                name: "communication",
                schema: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    media_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication", x => x.id);
                    table.CheckConstraint("ck_communication_media_type", "media_type in ('image','video')");
                    table.CheckConstraint("ck_communication_status", "status in ('draft','published','paused','expired','archived')");
                    table.CheckConstraint("ck_communication_window", "starts_at < ends_at");
                    table.ForeignKey(
                        name: "FK_communication_asset_media_asset_id",
                        column: x => x.media_asset_id,
                        principalSchema: "media",
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_communication_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_communication_establishment_id_id",
                schema: "communications",
                table: "communication",
                columns: new[] { "establishment_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_communication_establishment_id_status_priority_starts_at_en~",
                schema: "communications",
                table: "communication",
                columns: new[] { "establishment_id", "status", "priority", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_communication_media_asset_id",
                schema: "communications",
                table: "communication",
                column: "media_asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "communication",
                schema: "communications");
        }
    }
}
