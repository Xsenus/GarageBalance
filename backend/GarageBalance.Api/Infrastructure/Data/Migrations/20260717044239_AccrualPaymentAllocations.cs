using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccrualPaymentAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accrual_payment_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccrualId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accrual_payment_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accrual_payment_allocations_accruals_AccrualId",
                        column: x => x.AccrualId,
                        principalTable: "accruals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accrual_payment_allocations_financial_operations_FinancialO~",
                        column: x => x.FinancialOperationId,
                        principalTable: "financial_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accrual_payment_allocations_AccrualId",
                table: "accrual_payment_allocations",
                column: "AccrualId");

            migrationBuilder.CreateIndex(
                name: "IX_accrual_payment_allocations_FinancialOperationId_AccrualId",
                table: "accrual_payment_allocations",
                columns: new[] { "FinancialOperationId", "AccrualId" },
                unique: true,
                filter: "\"IsActive\" = true");

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    WITH accrual_ledger AS (
                        SELECT
                            a."Id", a."GarageId", a."IncomeTypeId", a."Amount",
                            SUM(a."Amount") OVER (
                                PARTITION BY a."GarageId", a."IncomeTypeId"
                                ORDER BY a."DueDate", a."AccountingMonth", a."CreatedAtUtc", a."Id"
                                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS amount_end
                        FROM accruals a
                        WHERE NOT a."IsCanceled" AND a."Amount" > 0
                    ),
                    payment_ledger AS (
                        SELECT
                            p."Id", p."GarageId", p."IncomeTypeId", p."Amount",
                            SUM(p."Amount") OVER (
                                PARTITION BY p."GarageId", p."IncomeTypeId"
                                ORDER BY p."OperationDate", p."CreatedAtUtc", p."Id"
                                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS amount_end
                        FROM financial_operations p
                        WHERE NOT p."IsCanceled"
                          AND p."OperationKind" = 'income'
                          AND p."GarageId" IS NOT NULL
                          AND p."IncomeTypeId" IS NOT NULL
                          AND p."Amount" > 0
                    ),
                    allocations AS (
                        SELECT
                            p."Id" AS payment_id,
                            a."Id" AS accrual_id,
                            GREATEST(
                                0,
                                LEAST(p.amount_end, a.amount_end)
                                - GREATEST(p.amount_end - p."Amount", a.amount_end - a."Amount")) AS allocated
                        FROM payment_ledger p
                        JOIN accrual_ledger a
                          ON a."GarageId" = p."GarageId"
                         AND a."IncomeTypeId" = p."IncomeTypeId"
                    )
                    INSERT INTO accrual_payment_allocations
                        ("Id", "FinancialOperationId", "AccrualId", "Amount", "IsActive", "CreatedAtUtc")
                    SELECT
                        md5(payment_id::text || ':' || accrual_id::text)::uuid,
                        payment_id,
                        accrual_id,
                        allocated,
                        TRUE,
                        CURRENT_TIMESTAMP
                    FROM allocations
                    WHERE allocated > 0;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accrual_payment_allocations");
        }
    }
}
