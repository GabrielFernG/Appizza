using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1707, CA1861 // EF-generated migration naming and inline column arrays.

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_EstablishmentsIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "establishments");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "establishment",
                schema: "establishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_identifier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    timezone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_establishment", x => x.id);
                    table.CheckConstraint("ck_establishment_status", "status in ('active','blocked','inactive')");
                });

            migrationBuilder.CreateTable(
                name: "permission",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    module = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "address",
                schema: "establishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    complement = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address", x => x.id);
                    table.ForeignKey(
                        name: "FK_address_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_hour",
                schema: "establishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    opening_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closing_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_hour", x => x.id);
                    table.CheckConstraint("ck_business_hour_day", "day_of_week between 0 and 6");
                    table.CheckConstraint("ck_business_hour_range", "opening_time <> closing_time");
                    table.ForeignKey(
                        name: "FK_business_hour_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.id);
                    table.CheckConstraint("ck_role_status", "status in ('active','inactive')");
                    table.ForeignKey(
                        name: "FK_role_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                schema: "establishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    setting_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    setting_value = table.Column<string>(type: "text", nullable: true),
                    value_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_setting", x => x.id);
                    table.CheckConstraint("ck_setting_value_type", "value_type in ('string','integer','boolean')");
                    table.ForeignKey(
                        name: "FK_setting_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    login = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    pin_hash = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_pin_attempts = table.Column<int>(type: "integer", nullable: false),
                    pin_locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.id);
                    table.CheckConstraint("ck_user_status", "status in ('active','blocked','inactive')");
                    table.ForeignKey(
                        name: "FK_user_establishment_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "establishments",
                        principalTable: "establishment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permission",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permission", x => x.id);
                    table.CheckConstraint("ck_role_permission_scope", "(scope_type is null and scope_id is null) or (scope_type is not null and scope_id is not null)");
                    table.ForeignKey(
                        name: "FK_role_permission_permission_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permission_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_permission",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission", x => x.id);
                    table.CheckConstraint("ck_user_permission_effect", "effect in ('allow','deny')");
                    table.CheckConstraint("ck_user_permission_scope", "(scope_type is null and scope_id is null) or (scope_type is not null and scope_id is not null)");
                    table.ForeignKey(
                        name: "FK_user_permission_permission_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_permission_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_role_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refresh_token_hash = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    replaced_by_session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_session", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_session_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_address_establishment_id",
                schema: "establishments",
                table: "address",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_hour_establishment_id_day_of_week_display_order",
                schema: "establishments",
                table: "business_hour",
                columns: new[] { "establishment_id", "day_of_week", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_establishment_public_code",
                schema: "establishments",
                table: "establishment",
                column: "public_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_establishment_status",
                schema: "establishments",
                table: "establishment",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_tax_identifier",
                schema: "establishments",
                table: "establishment",
                column: "tax_identifier",
                unique: true,
                filter: "tax_identifier is not null");

            migrationBuilder.CreateIndex(
                name: "ix_permission_code",
                schema: "identity",
                table: "permission",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_establishment_id_name",
                schema: "identity",
                table: "role",
                columns: new[] { "establishment_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_permission_id",
                schema: "identity",
                table: "role_permission",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_role_id_permission_id",
                schema: "identity",
                table: "role_permission",
                columns: new[] { "role_id", "permission_id" },
                unique: true,
                filter: "scope_type is null and scope_id is null");

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_role_id_permission_id_scope_type_scope_id",
                schema: "identity",
                table: "role_permission",
                columns: new[] { "role_id", "permission_id", "scope_type", "scope_id" },
                unique: true,
                filter: "scope_type is not null and scope_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_setting_establishment_id_setting_key",
                schema: "establishments",
                table: "setting",
                columns: new[] { "establishment_id", "setting_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_establishment_id_email",
                schema: "identity",
                table: "user",
                columns: new[] { "establishment_id", "email" },
                unique: true,
                filter: "email is not null");

            migrationBuilder.CreateIndex(
                name: "ix_user_establishment_id_login",
                schema: "identity",
                table: "user",
                columns: new[] { "establishment_id", "login" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_establishment_id_status",
                schema: "identity",
                table: "user",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_permission_id",
                schema: "identity",
                table: "user_permission",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_user_id_permission_id_effect",
                schema: "identity",
                table: "user_permission",
                columns: new[] { "user_id", "permission_id", "effect" },
                filter: "scope_type is null and scope_id is null");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_role_id",
                schema: "identity",
                table: "user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_user_id_role_id",
                schema: "identity",
                table: "user_role",
                columns: new[] { "user_id", "role_id" },
                unique: true,
                filter: "valid_from is null and valid_until is null");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_user_id_role_id_valid_from",
                schema: "identity",
                table: "user_role",
                columns: new[] { "user_id", "role_id", "valid_from" },
                unique: true,
                filter: "valid_from is not null");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_expires_at",
                schema: "identity",
                table: "user_session",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_refresh_token_hash",
                schema: "identity",
                table: "user_session",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_session_user_id_revoked_at",
                schema: "identity",
                table: "user_session",
                columns: new[] { "user_id", "revoked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address",
                schema: "establishments");

            migrationBuilder.DropTable(
                name: "business_hour",
                schema: "establishments");

            migrationBuilder.DropTable(
                name: "role_permission",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "establishments");

            migrationBuilder.DropTable(
                name: "user_permission",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_session",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "permission",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "establishment",
                schema: "establishments");
        }
    }
}
