using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    #pragma warning disable CA1707, CA1861
    /// <inheritdoc />
    public partial class Phase2_CatalogCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "category",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    image_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category", x => x.id);
                    table.CheckConstraint("ck_category_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_category_category_parent_category_id",
                        column: x => x.parent_category_id,
                        principalSchema: "catalog",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_category_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customization_group",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    selection_type = table.Column<string>(type: "text", nullable: false),
                    minimum_selections = table.Column<int>(type: "integer", nullable: false),
                    maximum_selections = table.Column<int>(type: "integer", nullable: true),
                    display_type = table.Column<string>(type: "text", nullable: false),
                    reusable = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customization_group", x => x.id);
                    table.CheckConstraint("ck_customization_group_limits", "minimum_selections >= 0 and (maximum_selections is null or maximum_selections >= minimum_selections)");
                    table.CheckConstraint("ck_customization_group_selection", "selection_type in ('single','multiple','quantity')");
                    table.CheckConstraint("ck_customization_group_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_customization_group_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingredient",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    kitchen_name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    default_additional_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    unit_of_measure = table.Column<string>(type: "text", nullable: true),
                    stock_control_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    image_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredient", x => x.id);
                    table.CheckConstraint("ck_ingredient_price", "default_additional_price >= 0");
                    table.CheckConstraint("ck_ingredient_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_ingredient_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingredient_attribute_definition",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    attribute_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredient_attribute_definition", x => x.id);
                    table.CheckConstraint("ck_ingredient_attribute_definition_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_ingredient_attribute_definition_establishment_establishment~",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    short_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    internal_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    primary_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_image_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    requires_production = table.Column<bool>(type: "boolean", nullable: false),
                    requires_operational_acceptance = table.Column<bool>(type: "boolean", nullable: false),
                    allows_notes = table.Column<bool>(type: "boolean", nullable: false),
                    maximum_note_length = table.Column<int>(type: "integer", nullable: true),
                    preparation_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estimated_preparation_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product", x => x.id);
                    table.CheckConstraint("ck_product_note_length", "maximum_note_length is null or maximum_note_length > 0");
                    table.CheckConstraint("ck_product_status", "status in ('active','inactive','archived')");
                    table.CheckConstraint("ck_product_type", "product_type in ('simple','configurable','pizza','custom_pizza','combo')");
                    table.ForeignKey(
                        name: "FK_product_category_primary_category_id",
                        column: x => x.primary_category_id,
                        principalSchema: "catalog",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingredient_attribute",
                schema: "catalog",
                columns: table => new
                {
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_boolean = table.Column<bool>(type: "boolean", nullable: true),
                    value_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredient_attribute", x => new { x.ingredient_id, x.attribute_definition_id });
                    table.ForeignKey(
                        name: "FK_ingredient_attribute_ingredient_attribute_definition_attrib~",
                        column: x => x.attribute_definition_id,
                        principalSchema: "catalog",
                        principalTable: "ingredient_attribute_definition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ingredient_attribute_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "catalog",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customization_option",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customization_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    price_rule_type = table.Column<string>(type: "text", nullable: false),
                    base_additional_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    linked_ingredient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customization_option", x => x.id);
                    table.CheckConstraint("ck_customization_option_link", "linked_ingredient_id is null or linked_product_id is null");
                    table.CheckConstraint("ck_customization_option_price", "base_additional_price >= 0");
                    table.CheckConstraint("ck_customization_option_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_customization_option_customization_group_customization_grou~",
                        column: x => x.customization_group_id,
                        principalSchema: "catalog",
                        principalTable: "customization_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customization_option_ingredient_linked_ingredient_id",
                        column: x => x.linked_ingredient_id,
                        principalSchema: "catalog",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customization_option_product_linked_product_id",
                        column: x => x.linked_product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_category",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_category", x => new { x.product_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_product_category_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_category_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_customization_group",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customization_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_override = table.Column<bool>(type: "boolean", nullable: true),
                    minimum_override = table.Column<int>(type: "integer", nullable: true),
                    maximum_override = table.Column<int>(type: "integer", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_customization_group", x => x.id);
                    table.CheckConstraint("ck_product_customization_group_limits", "(minimum_override is null or minimum_override >= 0) and (maximum_override is null or minimum_override is null or maximum_override >= minimum_override)");
                    table.ForeignKey(
                        name: "FK_product_customization_group_customization_group_customizati~",
                        column: x => x.customization_group_id,
                        principalSchema: "catalog",
                        principalTable: "customization_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_customization_group_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_ingredient",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    included_by_default = table.Column<bool>(type: "boolean", nullable: false),
                    required_for_recipe = table.Column<bool>(type: "boolean", nullable: false),
                    can_be_removed = table.Column<bool>(type: "boolean", nullable: false),
                    can_be_added = table.Column<bool>(type: "boolean", nullable: false),
                    default_quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    maximum_additional_quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    additional_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    application_scope = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_ingredient", x => x.id);
                    table.CheckConstraint("ck_product_ingredient_price", "additional_price >= 0");
                    table.CheckConstraint("ck_product_ingredient_quantity", "(default_quantity is null or default_quantity > 0) and (maximum_additional_quantity is null or maximum_additional_quantity > 0)");
                    table.CheckConstraint("ck_product_ingredient_required", "not required_for_recipe or (included_by_default and not can_be_removed)");
                    table.CheckConstraint("ck_product_ingredient_scope", "application_scope in ('whole_product','fraction','both')");
                    table.ForeignKey(
                        name: "FK_product_ingredient_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "catalog",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_ingredient_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variant",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    short_name = table.Column<string>(type: "text", nullable: true),
                    internal_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    barcode = table.Column<string>(type: "text", nullable: true),
                    base_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    image_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    estimated_preparation_minutes = table.Column<int>(type: "integer", nullable: true),
                    stock_control_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variant", x => x.id);
                    table.CheckConstraint("ck_product_variant_price", "base_price >= 0");
                    table.CheckConstraint("ck_product_variant_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_product_variant_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_customization_variant_rule",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_customization_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minimum_selections = table.Column<int>(type: "integer", nullable: true),
                    maximum_selections = table.Column<int>(type: "integer", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_customization_variant_rule", x => x.id);
                    table.CheckConstraint("ck_product_customization_variant_rule_limits", "(minimum_selections is null or minimum_selections >= 0) and (maximum_selections is null or minimum_selections is null or maximum_selections >= minimum_selections)");
                    table.ForeignKey(
                        name: "FK_product_customization_variant_rule_product_customization_gr~",
                        column: x => x.product_customization_group_id,
                        principalSchema: "catalog",
                        principalTable: "product_customization_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_customization_variant_rule_product_variant_product_~",
                        column: x => x.product_variant_id,
                        principalSchema: "catalog",
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_ingredient_override",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    included_by_default_override = table.Column<bool>(type: "boolean", nullable: true),
                    required_for_recipe_override = table.Column<bool>(type: "boolean", nullable: true),
                    can_be_removed_override = table.Column<bool>(type: "boolean", nullable: true),
                    can_be_added_override = table.Column<bool>(type: "boolean", nullable: true),
                    maximum_additional_quantity_override = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    additional_price_override = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    available = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variant_ingredient_override", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_variant_ingredient_override_product_ingredient_prod~",
                        column: x => x.product_ingredient_id,
                        principalSchema: "catalog",
                        principalTable: "product_ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_variant_ingredient_override_product_variant_product~",
                        column: x => x.product_variant_id,
                        principalSchema: "catalog",
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_category_establishment_id_parent_category_id_status_display~",
                schema: "catalog",
                table: "category",
                columns: new[] { "establishment_id", "parent_category_id", "status", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_category_parent_category_id",
                schema: "catalog",
                table: "category",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_customization_group_establishment_id_status_name",
                schema: "catalog",
                table: "customization_group",
                columns: new[] { "establishment_id", "status", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_customization_option_customization_group_id_status_display_~",
                schema: "catalog",
                table: "customization_option",
                columns: new[] { "customization_group_id", "status", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_customization_option_linked_ingredient_id",
                schema: "catalog",
                table: "customization_option",
                column: "linked_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_customization_option_linked_product_id",
                schema: "catalog",
                table: "customization_option",
                column: "linked_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_establishment_id_status_name",
                schema: "catalog",
                table: "ingredient",
                columns: new[] { "establishment_id", "status", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_attribute_attribute_definition_id",
                schema: "catalog",
                table: "ingredient_attribute",
                column: "attribute_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_attribute_definition_establishment_id_code",
                schema: "catalog",
                table: "ingredient_attribute_definition",
                columns: new[] { "establishment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_establishment_id_internal_code",
                schema: "catalog",
                table: "product",
                columns: new[] { "establishment_id", "internal_code" },
                unique: true,
                filter: "internal_code is not null");

            migrationBuilder.CreateIndex(
                name: "ix_product_establishment_id_status_display_order",
                schema: "catalog",
                table: "product",
                columns: new[] { "establishment_id", "status", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_product_primary_category_id",
                schema: "catalog",
                table: "product",
                column: "primary_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_category_category_id",
                schema: "catalog",
                table: "product_category",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_customization_group_customization_group_id",
                schema: "catalog",
                table: "product_customization_group",
                column: "customization_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_customization_group_product_id_customization_group_~",
                schema: "catalog",
                table: "product_customization_group",
                columns: new[] { "product_id", "customization_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_customization_variant_rule_product_customization_gr~",
                schema: "catalog",
                table: "product_customization_variant_rule",
                columns: new[] { "product_customization_group_id", "product_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_customization_variant_rule_product_variant_id",
                schema: "catalog",
                table: "product_customization_variant_rule",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_ingredient_ingredient_id",
                schema: "catalog",
                table: "product_ingredient",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_ingredient_product_id_ingredient_id",
                schema: "catalog",
                table: "product_ingredient",
                columns: new[] { "product_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_product_id_internal_code",
                schema: "catalog",
                table: "product_variant",
                columns: new[] { "product_id", "internal_code" },
                unique: true,
                filter: "internal_code is not null");

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_product_id_status_display_order",
                schema: "catalog",
                table: "product_variant",
                columns: new[] { "product_id", "status", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_ingredient_override_product_ingredient_id",
                schema: "catalog",
                table: "product_variant_ingredient_override",
                column: "product_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_ingredient_override_product_variant_id_prod~",
                schema: "catalog",
                table: "product_variant_ingredient_override",
                columns: new[] { "product_variant_id", "product_ingredient_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customization_option",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ingredient_attribute",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_category",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_customization_variant_rule",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variant_ingredient_override",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ingredient_attribute_definition",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_customization_group",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_ingredient",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variant",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "customization_group",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ingredient",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "category",
                schema: "catalog");
        }
    }
}
