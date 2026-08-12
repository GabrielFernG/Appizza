using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    #pragma warning disable CA1707, CA1861
    /// <inheritdoc />
    public partial class Phase2_CatalogPublicationMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.CreateTable(
                name: "asset",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset", x => x.id);
                    table.CheckConstraint("ck_media_asset_checksum", "length(checksum_sha256) = 64");
                    table.CheckConstraint("ck_media_asset_size", "file_size > 0");
                    table.CheckConstraint("ck_media_asset_status", "status in ('pending_upload','ready','failed','archived')");
                    table.ForeignKey(
                        name: "FK_asset_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalog_revision",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    semantic_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    validation_errors = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_revision", x => x.id);
                    table.CheckConstraint("ck_catalog_revision_status", "status in ('validating','published','rejected','superseded')");
                    table.ForeignKey(
                        name: "FK_catalog_revision_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingredient_availability",
                schema: "catalog",
                columns: table => new
                {
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    explicitly_available = table.Column<bool>(type: "boolean", nullable: false),
                    effectively_available = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredient_availability", x => x.ingredient_id);
                    table.ForeignKey(
                        name: "FK_ingredient_availability_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "catalog",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_availability",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    explicitly_available = table.Column<bool>(type: "boolean", nullable: false),
                    effectively_available = table.Column<bool>(type: "boolean", nullable: false),
                    derived_reason = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_availability", x => x.product_id);
                    table.ForeignKey(
                        name: "FK_product_availability_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_availability",
                schema: "catalog",
                columns: table => new
                {
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    explicitly_available = table.Column<bool>(type: "boolean", nullable: false),
                    effectively_available = table.Column<bool>(type: "boolean", nullable: false),
                    derived_reason = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variant_availability", x => x.product_variant_id);
                    table.ForeignKey(
                        name: "FK_product_variant_availability_product_variant_product_varian~",
                        column: x => x.product_variant_id,
                        principalSchema: "catalog",
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalog_state",
                schema: "catalog",
                columns: table => new
                {
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    availability_version = table.Column<long>(type: "bigint", nullable: false),
                    current_published_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_state", x => x.establishment_id);
                    table.CheckConstraint("ck_catalog_state_availability_version", "availability_version >= 0");
                    table.CheckConstraint("ck_catalog_state_catalog_version", "catalog_version >= 0");
                    table.ForeignKey(
                        name: "FK_catalog_state_catalog_revision_current_published_revision_id",
                        column: x => x.current_published_revision_id,
                        principalSchema: "catalog",
                        principalTable: "catalog_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_catalog_state_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_image_media_id",
                schema: "catalog",
                table: "product_variant",
                column: "image_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_primary_image_media_id",
                schema: "catalog",
                table: "product",
                column: "primary_image_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_image_media_id",
                schema: "catalog",
                table: "ingredient",
                column: "image_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_image_media_id",
                schema: "catalog",
                table: "category",
                column: "image_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_establishment_id_status",
                schema: "media",
                table: "asset",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_object_key",
                schema: "media",
                table: "asset",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_revision_establishment_id_catalog_version",
                schema: "catalog",
                table: "catalog_revision",
                columns: new[] { "establishment_id", "catalog_version" },
                unique: true,
                filter: "catalog_version is not null");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_revision_establishment_id_status",
                schema: "catalog",
                table: "catalog_revision",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_state_current_published_revision_id",
                schema: "catalog",
                table: "catalog_state",
                column: "current_published_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_availability_establishment_id_effectively_availa~",
                schema: "catalog",
                table: "ingredient_availability",
                columns: new[] { "establishment_id", "effectively_available" });

            migrationBuilder.CreateIndex(
                name: "ix_product_availability_establishment_id_effectively_available",
                schema: "catalog",
                table: "product_availability",
                columns: new[] { "establishment_id", "effectively_available" });

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_availability_establishment_id_effectively_a~",
                schema: "catalog",
                table: "product_variant_availability",
                columns: new[] { "establishment_id", "effectively_available" });

            migrationBuilder.AddForeignKey(
                name: "FK_category_asset_image_media_id",
                schema: "catalog",
                table: "category",
                column: "image_media_id",
                principalSchema: "media",
                principalTable: "asset",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ingredient_asset_image_media_id",
                schema: "catalog",
                table: "ingredient",
                column: "image_media_id",
                principalSchema: "media",
                principalTable: "asset",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_asset_primary_image_media_id",
                schema: "catalog",
                table: "product",
                column: "primary_image_media_id",
                principalSchema: "media",
                principalTable: "asset",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_variant_asset_image_media_id",
                schema: "catalog",
                table: "product_variant",
                column: "image_media_id",
                principalSchema: "media",
                principalTable: "asset",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_category_asset_image_media_id",
                schema: "catalog",
                table: "category");

            migrationBuilder.DropForeignKey(
                name: "FK_ingredient_asset_image_media_id",
                schema: "catalog",
                table: "ingredient");

            migrationBuilder.DropForeignKey(
                name: "FK_product_asset_primary_image_media_id",
                schema: "catalog",
                table: "product");

            migrationBuilder.DropForeignKey(
                name: "FK_product_variant_asset_image_media_id",
                schema: "catalog",
                table: "product_variant");

            migrationBuilder.DropTable(
                name: "asset",
                schema: "media");

            migrationBuilder.DropTable(
                name: "catalog_state",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ingredient_availability",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_availability",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variant_availability",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "catalog_revision",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "ix_product_variant_image_media_id",
                schema: "catalog",
                table: "product_variant");

            migrationBuilder.DropIndex(
                name: "ix_product_primary_image_media_id",
                schema: "catalog",
                table: "product");

            migrationBuilder.DropIndex(
                name: "ix_ingredient_image_media_id",
                schema: "catalog",
                table: "ingredient");

            migrationBuilder.DropIndex(
                name: "ix_category_image_media_id",
                schema: "catalog",
                table: "category");
        }
    }
}
