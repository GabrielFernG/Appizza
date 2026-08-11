using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1707, CA1861 // EF-generated migration naming and inline column arrays.

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_DevicesTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "devices");

            migrationBuilder.EnsureSchema(
                name: "tables");

            migrationBuilder.CreateSequence(
                name: "table_session_number_seq",
                schema: "tables");

            migrationBuilder.CreateTable(
                name: "device",
                schema: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    device_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    operating_system_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    app_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    credential_hash = table.Column<string>(type: "text", nullable: true),
                    credential_version = table.Column<int>(type: "integer", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device", x => x.id);
                    table.CheckConstraint("ck_device_establishment", "(status = 'awaiting_configuration' and establishment_id is null) or (status <> 'awaiting_configuration' and establishment_id is not null)");
                    table.CheckConstraint("ck_device_status", "status in ('awaiting_configuration','active','revoked','blocked')");
                    table.ForeignKey(
                        name: "FK_device_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sector",
                schema: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sector", x => x.id);
                    table.CheckConstraint("ck_sector_status", "status in ('active','inactive')");
                    table.ForeignKey(
                        name: "FK_sector_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_event",
                schema: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_event_device_device_id",
                        column: x => x.device_id,
                        principalSchema: "devices",
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_heartbeat",
                schema: "devices",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    battery_percentage = table.Column<int>(type: "integer", nullable: true),
                    network_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    storage_available_bytes = table.Column<long>(type: "bigint", nullable: true),
                    kiosk_mode_active = table.Column<bool>(type: "boolean", nullable: true),
                    sync_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_catalog_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_heartbeat", x => x.device_id);
                    table.CheckConstraint("ck_device_heartbeat_battery", "battery_percentage is null or battery_percentage between 0 and 100");
                    table.ForeignKey(
                        name: "FK_device_heartbeat_device_device_id",
                        column: x => x.device_id,
                        principalSchema: "devices",
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_session",
                schema: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    credential_version = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_session", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_session_device_device_id",
                        column: x => x.device_id,
                        principalSchema: "devices",
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dining_table",
                schema: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    internal_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_table", x => x.id);
                    table.CheckConstraint("ck_dining_table_capacity", "capacity is null or capacity > 0");
                    table.CheckConstraint("ck_dining_table_status", "status in ('available','occupied','closing','awaiting_cleaning','blocked','inactive')");
                    table.ForeignKey(
                        name: "FK_dining_table_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dining_table_sector_sector_id",
                        column: x => x.sector_id,
                        principalSchema: "tables",
                        principalTable: "sector",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_table_binding",
                schema: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bound_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unbound_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unbind_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_table_binding", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_table_binding_device_device_id",
                        column: x => x.device_id,
                        principalSchema: "devices",
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_table_binding_dining_table_dining_table_id",
                        column: x => x.dining_table_id,
                        principalSchema: "tables",
                        principalTable: "dining_table",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_session",
                schema: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_identification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_identification_resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closing_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_count = table.Column<int>(type: "integer", nullable: true),
                    subtotal_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    adjustment_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    service_charge_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    cover_charge_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reserved_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_session", x => x.id);
                    table.CheckConstraint("ck_table_session_identification", "customer_identification_status in ('pending','provided','skipped') and ((customer_identification_status = 'pending' and customer_identification_resolved_at is null) or (customer_identification_status <> 'pending' and customer_identification_resolved_at is not null))");
                    table.CheckConstraint("ck_table_session_status", "status in ('open','closing','awaiting_payment','partially_paid','paid','closed','suspended','cancelled')");
                    table.ForeignKey(
                        name: "FK_table_session_dining_table_dining_table_id",
                        column: x => x.dining_table_id,
                        principalSchema: "tables",
                        principalTable: "dining_table",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_table_session_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_customer_identification",
                schema: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identification_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: true),
                    encryption_nonce = table.Column<string>(type: "text", nullable: true),
                    encryption_tag = table.Column<string>(type: "text", nullable: true),
                    value_hash = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    masked_value = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    purpose = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retention_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    anonymized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_customer_identification", x => x.id);
                    table.ForeignKey(
                        name: "FK_session_customer_identification_table_session_table_session~",
                        column: x => x.table_session_id,
                        principalSchema: "tables",
                        principalTable: "table_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_session_status_history",
                schema: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    new_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_session_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_session_status_history_table_session_table_session_id",
                        column: x => x.table_session_id,
                        principalSchema: "tables",
                        principalTable: "table_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_establishment_id_status",
                schema: "devices",
                table: "device",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_device_installation_id",
                schema: "devices",
                table: "device",
                column: "installation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_last_seen_at",
                schema: "devices",
                table: "device",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_event_device_id_occurred_at",
                schema: "devices",
                table: "device_event",
                columns: new[] { "device_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_session_device_id_revoked_at",
                schema: "devices",
                table: "device_session",
                columns: new[] { "device_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_session_expires_at",
                schema: "devices",
                table: "device_session",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_session_refresh_token_hash",
                schema: "devices",
                table: "device_session",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_table_binding_device_id",
                schema: "devices",
                table: "device_table_binding",
                column: "device_id",
                unique: true,
                filter: "unbound_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_device_table_binding_dining_table_id_unbound_at",
                schema: "devices",
                table: "device_table_binding",
                columns: new[] { "dining_table_id", "unbound_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dining_table_establishment_id_internal_code",
                schema: "tables",
                table: "dining_table",
                columns: new[] { "establishment_id", "internal_code" },
                unique: true,
                filter: "internal_code is not null");

            migrationBuilder.CreateIndex(
                name: "ix_dining_table_establishment_id_status",
                schema: "tables",
                table: "dining_table",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_dining_table_sector_id",
                schema: "tables",
                table: "dining_table",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "ix_sector_establishment_id_display_order",
                schema: "tables",
                table: "sector",
                columns: new[] { "establishment_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_session_customer_identification_retention_until_anonymized_~",
                schema: "tables",
                table: "session_customer_identification",
                columns: new[] { "retention_until", "anonymized_at" });

            migrationBuilder.CreateIndex(
                name: "ix_session_customer_identification_table_session_id_purpose",
                schema: "tables",
                table: "session_customer_identification",
                columns: new[] { "table_session_id", "purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_session_dining_table_id",
                schema: "tables",
                table: "table_session",
                column: "dining_table_id",
                unique: true,
                filter: "status in ('open','closing','awaiting_payment','partially_paid','paid','suspended')");

            migrationBuilder.CreateIndex(
                name: "ix_table_session_dining_table_id_status",
                schema: "tables",
                table: "table_session",
                columns: new[] { "dining_table_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_table_session_establishment_id_session_number",
                schema: "tables",
                table: "table_session",
                columns: new[] { "establishment_id", "session_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_session_establishment_id_status",
                schema: "tables",
                table: "table_session",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_table_session_status_history_table_session_id_changed_at",
                schema: "tables",
                table: "table_session_status_history",
                columns: new[] { "table_session_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_event",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "device_heartbeat",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "device_session",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "device_table_binding",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "session_customer_identification",
                schema: "tables");

            migrationBuilder.DropTable(
                name: "table_session_status_history",
                schema: "tables");

            migrationBuilder.DropTable(
                name: "device",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "table_session",
                schema: "tables");

            migrationBuilder.DropTable(
                name: "dining_table",
                schema: "tables");

            migrationBuilder.DropTable(
                name: "sector",
                schema: "tables");

            migrationBuilder.DropSequence(
                name: "table_session_number_seq",
                schema: "tables");
        }
    }
}
