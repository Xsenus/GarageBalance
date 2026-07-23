using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCashBankTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_bank_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsCanceled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_bank_transfers", x => x.Id);
                    table.CheckConstraint("CK_cash_bank_transfers_Amount", "\"Amount\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_transfers_ActorUserId",
                table: "cash_bank_transfers",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_transfers_CreatedAtUtc",
                table: "cash_bank_transfers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_transfers_IsCanceled",
                table: "cash_bank_transfers",
                column: "IsCanceled");

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_transfers_TransferDate",
                table: "cash_bank_transfers",
                column: "TransferDate");

            migrationBuilder.Sql(
                """
                INSERT INTO cash_bank_transfers (
                    "Id", "TransferDate", "Amount", "Comment", "IsCanceled",
                    "ActorUserId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT
                    operation."Id",
                    COALESCE(
                        substring(
                            operation."Reason"
                            FROM '^Сдача кассы в банк ([0-9]{4}-[0-9]{2}-[0-9]{2})')::date,
                        (operation."CreatedAtUtc" AT TIME ZONE 'UTC')::date),
                    operation."Amount",
                    CASE
                        WHEN operation."Reason" ~ '^Сдача кассы в банк [0-9]{4}-[0-9]{2}-[0-9]{2}'
                        THEN NULLIF(
                            regexp_replace(
                                operation."Reason",
                                '^Сдача кассы в банк [0-9]{4}-[0-9]{2}-[0-9]{2}:?[ ]?',
                                ''),
                            '')
                        ELSE operation."Reason"
                    END,
                    operation."IsCanceled",
                    operation."ActorUserId",
                    operation."CreatedAtUtc",
                    operation."UpdatedAtUtc"
                FROM fund_operations operation
                WHERE operation."IsCashToBankTransfer" = TRUE;

                DELETE FROM fund_operations
                WHERE "IsCashToBankTransfer" = TRUE;

                DO $$
                DECLARE
                    fund_record RECORD;
                    operation_record RECORD;
                    running_balance numeric(18, 2);
                BEGIN
                    FOR fund_record IN SELECT "Id" FROM funds
                    LOOP
                        running_balance := 0;
                        FOR operation_record IN
                            SELECT "Id", "OperationKind", "Amount", "IsCanceled"
                            FROM fund_operations
                            WHERE "FundId" = fund_record."Id"
                            ORDER BY "CreatedAtUtc", "Id"
                        LOOP
                            IF NOT operation_record."IsCanceled" THEN
                                UPDATE fund_operations
                                SET "BalanceBefore" = running_balance
                                WHERE "Id" = operation_record."Id";

                                IF operation_record."OperationKind" = 'deposit' THEN
                                    running_balance := running_balance + operation_record."Amount";
                                ELSE
                                    running_balance := running_balance - operation_record."Amount";
                                END IF;

                                UPDATE fund_operations
                                SET "BalanceAfter" = running_balance
                                WHERE "Id" = operation_record."Id";
                            END IF;
                        END LOOP;

                        UPDATE funds
                        SET "Balance" = running_balance,
                            "UpdatedAtUtc" = CURRENT_TIMESTAMP
                        WHERE "Id" = fund_record."Id";
                    END LOOP;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "IsCashToBankTransfer",
                table: "fund_operations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_bank_transfers");

            migrationBuilder.AddColumn<bool>(
                name: "IsCashToBankTransfer",
                table: "fund_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
