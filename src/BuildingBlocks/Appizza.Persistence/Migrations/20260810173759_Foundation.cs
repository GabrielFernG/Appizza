using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core migration API requires inline column arrays.

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "establishments");
            migrationBuilder.EnsureSchema(name: "identity");
            migrationBuilder.EnsureSchema(name: "catalog");
            migrationBuilder.EnsureSchema(name: "promotions");
            migrationBuilder.EnsureSchema(name: "media");
            migrationBuilder.EnsureSchema(name: "communications");
            migrationBuilder.EnsureSchema(name: "tables");
            migrationBuilder.EnsureSchema(name: "ordering");
            migrationBuilder.EnsureSchema(name: "kitchen");
            migrationBuilder.EnsureSchema(name: "payments");
            migrationBuilder.EnsureSchema(name: "devices");
            migrationBuilder.EnsureSchema(name: "operations");
            migrationBuilder.EnsureSchema(name: "reporting");
            migrationBuilder.EnsureSchema(name: "auditing");
            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.CreateTable(
                name: "idempotency_record",
                schema: "integration",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    operation_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_hash = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_payload = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_record", x => new { x.idempotency_key, x.operation_type });
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                schema: "integration",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_message", x => new { x.event_id, x.consumer_name });
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_occurred",
                schema: "integration",
                table: "outbox_message",
                columns: new[] { "event_type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "integration",
                table: "outbox_message",
                columns: new[] { "processed_at", "next_retry_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_record",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "inbox_message",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "integration");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS auditing");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS reporting");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS operations");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS devices");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS payments");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS kitchen");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS ordering");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS tables");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS communications");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS media");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS promotions");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS catalog");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS identity");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS establishments");
        }
    }
}
