using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appizza.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7B1ProviderExecutionDurable : Migration
    {
        private static readonly string[] EstablishmentAttemptColumns = ["establishment_id", "payment_attempt_id"];
        private static readonly string[] ClaimColumns = ["establishment_id", "status", "claimed_until"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_provider_execution",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    provider_idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lifecycle_applied = table.Column<bool>(type: "boolean", nullable: false),
                    reconciliation_required = table.Column<bool>(type: "boolean", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error_classification = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_provider_execution", x => x.id);
                    table.CheckConstraint("ck_payment_provider_execution_status", "status in ('pending','processing','awaiting_customer_action','unknown','outcome_observed','completed','terminal_failure')");
                    table.ForeignKey(
                        name: "FK_payment_provider_execution_payment_attempt_payment_attempt_~",
                        column: x => x.payment_attempt_id,
                        principalSchema: "payments",
                        principalTable: "payment_attempt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_provider_execution_establishment_id_payment_attempt~",
                schema: "payments",
                table: "payment_provider_execution",
                columns: EstablishmentAttemptColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_provider_execution_establishment_id_status_claimed_~",
                schema: "payments",
                table: "payment_provider_execution",
                columns: ClaimColumns);

            migrationBuilder.CreateIndex(
                name: "ix_payment_provider_execution_payment_attempt_id",
                schema: "payments",
                table: "payment_provider_execution",
                column: "payment_attempt_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_provider_execution",
                schema: "payments");
        }
    }
}
