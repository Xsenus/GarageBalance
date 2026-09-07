using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensePaymentBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_payment_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_payment_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expense_payment_batches_app_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_payment_batch_operations",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_payment_batch_operations", x => new { x.BatchId, x.OperationId });
                    table.ForeignKey(
                        name: "FK_expense_payment_batch_operations_expense_payment_batches_Ba~",
                        column: x => x.BatchId,
                        principalTable: "expense_payment_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_payment_batch_operations_financial_operations_Opera~",
                        column: x => x.OperationId,
                        principalTable: "financial_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_payment_batch_operations_OperationId",
                table: "expense_payment_batch_operations",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_payment_batches_ActorUserId_CreatedAtUtc",
                table: "expense_payment_batches",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $batch_history$
                BEGIN
                    IF EXISTS (SELECT 1 FROM expense_payment_batches) THEN
                        RAISE EXCEPTION 'Нельзя удалить историю выполненных массовых выплат при откате миграции.';
                    END IF;
                END $batch_history$;
                """);
            migrationBuilder.DropTable(
                name: "expense_payment_batch_operations");

            migrationBuilder.DropTable(
                name: "expense_payment_batches");
        }
    }
}
