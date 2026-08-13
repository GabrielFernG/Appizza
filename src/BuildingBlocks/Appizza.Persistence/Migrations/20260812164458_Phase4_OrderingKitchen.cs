using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    #pragma warning disable CA1707, CA1861
    /// <inheritdoc />
    public partial class Phase4_OrderingKitchen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_record",
                schema: "integration",
                table: "idempotency_record");

            migrationBuilder.EnsureSchema(
                name: "ordering");

            migrationBuilder.EnsureSchema(
                name: "kitchen");

            migrationBuilder.CreateSequence(
                name: "order_number_seq",
                schema: "ordering");

            migrationBuilder.CreateSequence(
                name: "production_queue_position_seq",
                schema: "kitchen");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "integration",
                table: "idempotency_record",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_record",
                schema: "integration",
                table: "idempotency_record",
                column: "id");

            migrationBuilder.CreateTable(
                name: "cart_simulation",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    availability_version = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    simulation_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    can_submit = table.Column<bool>(type: "boolean", nullable: false),
                    intent_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    result_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_simulation", x => x.id);
                    table.CheckConstraint("ck_cart_simulation_versions", "catalog_version >= 0 and availability_version >= 0");
                    table.ForeignKey(
                        name: "FK_cart_simulation_device_source_device_id",
                        column: x => x.source_device_id,
                        principalSchema: "devices",
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cart_simulation_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cart_simulation_table_session_table_session_id",
                        column: x => x.table_session_id,
                        principalSchema: "tables",
                        principalTable: "table_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_order",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('ordering.order_number_seq')"),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_order", x => x.id);
                    table.CheckConstraint("ck_customer_order_amounts", "subtotal_amount >= 0 and discount_amount = 0 and total_amount >= 0");
                    table.CheckConstraint("ck_customer_order_status", "status in ('submitted')");
                    table.ForeignKey(
                        name: "FK_customer_order_device_source_device_id",
                        column: x => x.source_device_id,
                        principalSchema: "devices",
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_order_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_order_table_session_table_session_id",
                        column: x => x.table_session_id,
                        principalSchema: "tables",
                        principalTable: "table_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "station",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    station_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    default_target_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station", x => x.id);
                    table.CheckConstraint("ck_station_status", "status in ('active','inactive')");
                    table.ForeignKey(
                        name: "FK_station_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_cart_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    product_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    configuration_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    commercial_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    catalog_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    availability_version = table.Column<long>(type: "bigint", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot_schema_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item", x => x.id);
                    table.CheckConstraint("ck_order_item_quantity", "quantity > 0");
                    table.CheckConstraint("ck_order_item_status", "commercial_status in ('submitted','partially_cancelled','cancelled','completed')");
                    table.ForeignKey(
                        name: "FK_order_item_customer_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "customer_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_combo_selection",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_group_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selected_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    component_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_combo_selection", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_item_combo_selection_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_ingredient",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    additional_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_ingredient", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_item_ingredient_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_note",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_note", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_item_note_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_option",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    additional_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_option", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_item_option_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_pizza_configuration",
                schema: "ordering",
                columns: table => new
                {
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    dough_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dough_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    crust_id = table.Column<Guid>(type: "uuid", nullable: true),
                    crust_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    fraction_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_pizza_configuration", x => x.order_item_id);
                    table.ForeignKey(
                        name: "FK_order_item_pizza_configuration_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_pizza_fraction",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    flavor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flavor_name = table.Column<string>(type: "text", nullable: true),
                    is_custom = table.Column<bool>(type: "boolean", nullable: false),
                    fraction_numerator = table.Column<int>(type: "integer", nullable: false),
                    fraction_denominator = table.Column<int>(type: "integer", nullable: false),
                    reference_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_pizza_fraction", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_item_pizza_fraction_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_item",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    queue_position = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('kitchen.production_queue_position_seq')"),
                    requires_production = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_item", x => x.id);
                    table.CheckConstraint("ck_production_item_phase4_status", "status in ('awaiting_acceptance','accepted','awaiting_preparation')");
                    table.ForeignKey(
                        name: "FK_production_item_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_item_order_item_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "ordering",
                        principalTable: "order_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_item_station_station_id",
                        column: x => x.station_id,
                        principalSchema: "kitchen",
                        principalTable: "station",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_status_history",
                schema: "kitchen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    new_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_production_status_history_production_item_production_item_id",
                        column: x => x.production_item_id,
                        principalSchema: "kitchen",
                        principalTable: "production_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_establishment_id_operation_type_idempote~",
                schema: "integration",
                table: "idempotency_record",
                columns: new[] { "establishment_id", "operation_type", "idempotency_key" },
                unique: true,
                filter: "establishment_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_operation_type_idempotency_key",
                schema: "integration",
                table: "idempotency_record",
                columns: new[] { "operation_type", "idempotency_key" },
                unique: true,
                filter: "establishment_id is null");

            migrationBuilder.CreateIndex(
                name: "ix_cart_simulation_establishment_id_source_device_id_local_car~",
                schema: "ordering",
                table: "cart_simulation",
                columns: new[] { "establishment_id", "source_device_id", "local_cart_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cart_simulation_establishment_id_table_session_id_valid_unt~",
                schema: "ordering",
                table: "cart_simulation",
                columns: new[] { "establishment_id", "table_session_id", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_cart_simulation_source_device_id",
                schema: "ordering",
                table: "cart_simulation",
                column: "source_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_simulation_table_session_id",
                schema: "ordering",
                table: "cart_simulation",
                column: "table_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_establishment_id_source_device_id_client_sub~",
                schema: "ordering",
                table: "customer_order",
                columns: new[] { "establishment_id", "source_device_id", "client_submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_establishment_id_table_session_id_submitted_~",
                schema: "ordering",
                table: "customer_order",
                columns: new[] { "establishment_id", "table_session_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_order_number",
                schema: "ordering",
                table: "customer_order",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_source_device_id",
                schema: "ordering",
                table: "customer_order",
                column: "source_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_table_session_id",
                schema: "ordering",
                table: "customer_order",
                column: "table_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_order_id_local_cart_item_id",
                schema: "ordering",
                table: "order_item",
                columns: new[] { "order_id", "local_cart_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_combo_selection_order_item_id_combo_group_id_com~",
                schema: "ordering",
                table: "order_item_combo_selection",
                columns: new[] { "order_item_id", "combo_group_id", "combo_group_item_id", "selected_product_id", "selected_variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_ingredient_order_item_id_ingredient_id_action",
                schema: "ordering",
                table: "order_item_ingredient",
                columns: new[] { "order_item_id", "ingredient_id", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_note_order_item_id_position",
                schema: "ordering",
                table: "order_item_note",
                columns: new[] { "order_item_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_option_order_item_id_option_id",
                schema: "ordering",
                table: "order_item_option",
                columns: new[] { "order_item_id", "option_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_pizza_fraction_order_item_id_position",
                schema: "ordering",
                table: "order_item_pizza_fraction",
                columns: new[] { "order_item_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_item_establishment_id_station_id_status_queue_po~",
                schema: "kitchen",
                table: "production_item",
                columns: new[] { "establishment_id", "station_id", "status", "queue_position" });

            migrationBuilder.CreateIndex(
                name: "ix_production_item_order_item_id",
                schema: "kitchen",
                table: "production_item",
                column: "order_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_item_station_id",
                schema: "kitchen",
                table: "production_item",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_status_history_production_item_id_changed_at",
                schema: "kitchen",
                table: "production_status_history",
                columns: new[] { "production_item_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_station_establishment_id",
                schema: "kitchen",
                table: "station",
                column: "establishment_id",
                unique: true,
                filter: "is_default and status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_station_establishment_id_name",
                schema: "kitchen",
                table: "station",
                columns: new[] { "establishment_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_simulation",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_item_combo_selection",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_item_ingredient",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_item_note",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_item_option",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_item_pizza_configuration",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_item_pizza_fraction",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "production_status_history",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "production_item",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "order_item",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "station",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "customer_order",
                schema: "ordering");

            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_record",
                schema: "integration",
                table: "idempotency_record");

            migrationBuilder.DropIndex(
                name: "ix_idempotency_record_establishment_id_operation_type_idempote~",
                schema: "integration",
                table: "idempotency_record");

            migrationBuilder.DropIndex(
                name: "ix_idempotency_record_operation_type_idempotency_key",
                schema: "integration",
                table: "idempotency_record");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "integration",
                table: "idempotency_record");

            migrationBuilder.DropSequence(
                name: "order_number_seq",
                schema: "ordering");

            migrationBuilder.DropSequence(
                name: "production_queue_position_seq",
                schema: "kitchen");

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_record",
                schema: "integration",
                table: "idempotency_record",
                columns: new[] { "idempotency_key", "operation_type" });
        }
    }
}
