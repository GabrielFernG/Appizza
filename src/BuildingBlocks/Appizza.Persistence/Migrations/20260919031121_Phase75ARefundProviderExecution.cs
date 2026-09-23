using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase75ARefundProviderExecution : Migration
    {
        private static readonly string[] ProviderKeyColumns = ["establishment_id", "provider", "provider_idempotency_key"];
        private static readonly string[] RefundColumns = ["establishment_id", "refund_id"];
        private static readonly string[] StatusClaimColumns = ["establishment_id", "status", "claimed_until"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refund_provider_execution",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    provider_idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reconciliation_required = table.Column<bool>(type: "boolean", nullable: false),
                    last_error_classification = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    claimed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lifecycle_applied = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_provider_execution", x => x.id);
                    table.CheckConstraint("ck_refund_provider_execution_status", "status in ('pending','processing','unknown','outcome_observed','completed','terminal_failure')");
                    table.ForeignKey(
                        name: "FK_refund_provider_execution_refund_refund_id",
                        column: x => x.refund_id,
                        principalSchema: "payments",
                        principalTable: "refund",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refund_provider_execution_establishment_id_provider_provide~",
                schema: "payments",
                table: "refund_provider_execution",
                columns: ProviderKeyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refund_provider_execution_establishment_id_refund_id",
                schema: "payments",
                table: "refund_provider_execution",
                columns: RefundColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refund_provider_execution_establishment_id_status_claimed_u~",
                schema: "payments",
                table: "refund_provider_execution",
                columns: StatusClaimColumns);

            migrationBuilder.CreateIndex(
                name: "ix_refund_provider_execution_refund_id",
                schema: "payments",
                table: "refund_provider_execution",
                column: "refund_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refund_provider_execution",
                schema: "payments");
        }
    }
}
