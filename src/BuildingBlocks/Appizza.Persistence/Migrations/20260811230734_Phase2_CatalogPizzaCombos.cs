using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    #pragma warning disable CA1707, CA1861
    /// <inheritdoc />
    public partial class Phase2_CatalogPizzaCombos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "combo",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_strategy = table.Column<string>(type: "text", nullable: false),
                    fixed_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    discount_type = table.Column<string>(type: "text", nullable: true),
                    discount_value = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combo", x => x.id);
                    table.CheckConstraint("ck_combo_fixed", "pricing_strategy <> 'fixed' or fixed_price is not null");
                    table.CheckConstraint("ck_combo_prices", "(fixed_price is null or fixed_price >= 0) and (discount_value is null or discount_value >= 0)");
                    table.ForeignKey(
                        name: "FK_combo_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crust",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    image_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crust", x => x.id);
                    table.CheckConstraint("ck_crust_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_crust_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dough",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dough", x => x.id);
                    table.CheckConstraint("ck_dough_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_dough_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pizza_flavor",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pizza_flavor", x => x.id);
                    table.CheckConstraint("ck_pizza_flavor_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_pizza_flavor_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pizza_size",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    short_name = table.Column<string>(type: "text", nullable: true),
                    slice_count = table.Column<int>(type: "integer", nullable: true),
                    diameter_cm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pizza_size", x => x.id);
                    table.CheckConstraint("ck_pizza_size_status", "status in ('active','inactive','archived')");
                    table.CheckConstraint("ck_pizza_size_values", "(slice_count is null or slice_count > 0) and (diameter_cm is null or diameter_cm > 0)");
                    table.ForeignKey(
                        name: "FK_pizza_size_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "combo_group",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    minimum_items = table.Column<int>(type: "integer", nullable: false),
                    maximum_items = table.Column<int>(type: "integer", nullable: false),
                    allow_repetition = table.Column<bool>(type: "boolean", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combo_group", x => x.id);
                    table.CheckConstraint("ck_combo_group_limits", "minimum_items >= 0 and maximum_items >= minimum_items and maximum_items > 0");
                    table.ForeignKey(
                        name: "FK_combo_group_combo_combo_id",
                        column: x => x.combo_id,
                        principalSchema: "catalog",
                        principalTable: "combo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pizza_crust",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pizza_crust", x => new { x.product_id, x.crust_id });
                    table.ForeignKey(
                        name: "FK_pizza_crust_crust_crust_id",
                        column: x => x.crust_id,
                        principalSchema: "catalog",
                        principalTable: "crust",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pizza_crust_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pizza_dough",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dough_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pizza_dough", x => new { x.product_id, x.dough_id });
                    table.ForeignKey(
                        name: "FK_pizza_dough_dough_dough_id",
                        column: x => x.dough_id,
                        principalSchema: "catalog",
                        principalTable: "dough",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pizza_dough_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crust_size_price",
                schema: "catalog",
                columns: table => new
                {
                    crust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizza_size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    additional_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crust_size_price", x => new { x.crust_id, x.pizza_size_id });
                    table.CheckConstraint("ck_crust_size_price", "additional_price >= 0");
                    table.ForeignKey(
                        name: "FK_crust_size_price_crust_crust_id",
                        column: x => x.crust_id,
                        principalSchema: "catalog",
                        principalTable: "crust",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_crust_size_price_pizza_size_pizza_size_id",
                        column: x => x.pizza_size_id,
                        principalSchema: "catalog",
                        principalTable: "pizza_size",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "custom_pizza_base_price",
                schema: "catalog",
                columns: table => new
                {
                    custom_pizza_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizza_size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_pizza_base_price", x => new { x.custom_pizza_product_id, x.pizza_size_id });
                    table.CheckConstraint("ck_custom_pizza_base_price", "base_price >= 0");
                    table.ForeignKey(
                        name: "FK_custom_pizza_base_price_pizza_size_pizza_size_id",
                        column: x => x.pizza_size_id,
                        principalSchema: "catalog",
                        principalTable: "pizza_size",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_custom_pizza_base_price_product_custom_pizza_product_id",
                        column: x => x.custom_pizza_product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dough_size_price",
                schema: "catalog",
                columns: table => new
                {
                    dough_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizza_size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    additional_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dough_size_price", x => new { x.dough_id, x.pizza_size_id });
                    table.CheckConstraint("ck_dough_size_price", "additional_price >= 0");
                    table.ForeignKey(
                        name: "FK_dough_size_price_dough_dough_id",
                        column: x => x.dough_id,
                        principalSchema: "catalog",
                        principalTable: "dough",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dough_size_price_pizza_size_pizza_size_id",
                        column: x => x.pizza_size_id,
                        principalSchema: "catalog",
                        principalTable: "pizza_size",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pizza_flavor_price",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizza_flavor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizza_size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false),
                    estimated_preparation_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pizza_flavor_price", x => x.id);
                    table.CheckConstraint("ck_pizza_flavor_price", "price >= 0");
                    table.ForeignKey(
                        name: "FK_pizza_flavor_price_pizza_flavor_pizza_flavor_id",
                        column: x => x.pizza_flavor_id,
                        principalSchema: "catalog",
                        principalTable: "pizza_flavor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pizza_flavor_price_pizza_size_pizza_size_id",
                        column: x => x.pizza_size_id,
                        principalSchema: "catalog",
                        principalTable: "pizza_size",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pizza_product_size",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizza_size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false),
                    maximum_flavor_count = table.Column<int>(type: "integer", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pizza_product_size", x => new { x.product_id, x.pizza_size_id });
                    table.CheckConstraint("ck_pizza_product_size_flavors", "maximum_flavor_count is null or maximum_flavor_count > 0");
                    table.ForeignKey(
                        name: "FK_pizza_product_size_pizza_size_pizza_size_id",
                        column: x => x.pizza_size_id,
                        principalSchema: "catalog",
                        principalTable: "pizza_size",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pizza_product_size_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "combo_group_item",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inclusion_type = table.Column<string>(type: "text", nullable: false),
                    additional_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    fixed_quantity = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combo_group_item", x => x.id);
                    table.CheckConstraint("ck_combo_group_item_price", "additional_price >= 0");
                    table.CheckConstraint("ck_combo_group_item_quantity", "fixed_quantity is null or fixed_quantity > 0");
                    table.CheckConstraint("ck_combo_group_item_selector", "((product_id is not null)::int + (product_variant_id is not null)::int + (category_id is not null)::int) = 1");
                    table.CheckConstraint("ck_combo_group_item_status", "status in ('active','inactive','archived')");
                    table.ForeignKey(
                        name: "FK_combo_group_item_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_combo_group_item_combo_group_combo_group_id",
                        column: x => x.combo_group_id,
                        principalSchema: "catalog",
                        principalTable: "combo_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_combo_group_item_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_combo_group_item_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalSchema: "catalog",
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "combo_item_restriction",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_group_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    restriction_type = table.Column<string>(type: "text", nullable: false),
                    referenced_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combo_item_restriction", x => x.id);
                    table.ForeignKey(
                        name: "FK_combo_item_restriction_combo_group_item_combo_group_item_id",
                        column: x => x.combo_group_item_id,
                        principalSchema: "catalog",
                        principalTable: "combo_group_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_combo_product_id",
                schema: "catalog",
                table: "combo",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_combo_group_combo_id_display_order",
                schema: "catalog",
                table: "combo_group",
                columns: new[] { "combo_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_combo_group_item_category_id",
                schema: "catalog",
                table: "combo_group_item",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_combo_group_item_combo_group_id",
                schema: "catalog",
                table: "combo_group_item",
                column: "combo_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_combo_group_item_product_id",
                schema: "catalog",
                table: "combo_group_item",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_combo_group_item_product_variant_id",
                schema: "catalog",
                table: "combo_group_item",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_combo_item_restriction_combo_group_item_id_restriction_type",
                schema: "catalog",
                table: "combo_item_restriction",
                columns: new[] { "combo_group_item_id", "restriction_type" });

            migrationBuilder.CreateIndex(
                name: "ix_crust_establishment_id_status",
                schema: "catalog",
                table: "crust",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_crust_size_price_pizza_size_id",
                schema: "catalog",
                table: "crust_size_price",
                column: "pizza_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_pizza_base_price_pizza_size_id",
                schema: "catalog",
                table: "custom_pizza_base_price",
                column: "pizza_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_dough_establishment_id_status",
                schema: "catalog",
                table: "dough",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_dough_size_price_pizza_size_id",
                schema: "catalog",
                table: "dough_size_price",
                column: "pizza_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_pizza_crust_crust_id",
                schema: "catalog",
                table: "pizza_crust",
                column: "crust_id");

            migrationBuilder.CreateIndex(
                name: "ix_pizza_dough_dough_id",
                schema: "catalog",
                table: "pizza_dough",
                column: "dough_id");

            migrationBuilder.CreateIndex(
                name: "ix_pizza_flavor_product_id",
                schema: "catalog",
                table: "pizza_flavor",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pizza_flavor_price_pizza_flavor_id_pizza_size_id",
                schema: "catalog",
                table: "pizza_flavor_price",
                columns: new[] { "pizza_flavor_id", "pizza_size_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pizza_flavor_price_pizza_size_id",
                schema: "catalog",
                table: "pizza_flavor_price",
                column: "pizza_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_pizza_product_size_pizza_size_id",
                schema: "catalog",
                table: "pizza_product_size",
                column: "pizza_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_pizza_size_establishment_id_status_display_order",
                schema: "catalog",
                table: "pizza_size",
                columns: new[] { "establishment_id", "status", "display_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "combo_item_restriction",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "crust_size_price",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "custom_pizza_base_price",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "dough_size_price",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pizza_crust",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pizza_dough",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pizza_flavor_price",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pizza_product_size",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "combo_group_item",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "crust",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "dough",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pizza_flavor",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pizza_size",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "combo_group",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "combo",
                schema: "catalog");
        }
    }
}
