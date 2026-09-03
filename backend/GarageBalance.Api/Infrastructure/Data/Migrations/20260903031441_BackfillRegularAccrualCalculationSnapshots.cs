using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillRegularAccrualCalculationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE accruals
                SET "CalculationDetailsJson" = jsonb_build_object(
                    'version', 0,
                    'accountingMonth', to_char("AccountingMonth", 'YYYY-MM-DD'),
                    'previousMeterValue', NULL,
                    'currentMeterValue', NULL,
                    'meterConsumption', NULL,
                    'requiresMeter', "RequiresMeterReading",
                    'volumeAllocationRule', NULL,
                    'lines', jsonb_build_array(),
                    'totalAmount', "Amount",
                    'averageRate', NULL,
                    'rateAveragingRule', NULL,
                    'monthlyCalculationFormula', 'Историческая сумма сохранена без изменения; исходная формула в старых данных отсутствовала.'
                )
                WHERE "Source" = 'regular'
                  AND "CalculationDetailsJson" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE accruals
                SET "CalculationDetailsJson" = NULL
                WHERE "Source" = 'regular'
                  AND "CalculationDetailsJson" ->> 'version' = '0'
                  AND "CalculationDetailsJson" ->> 'monthlyCalculationFormula' = 'Историческая сумма сохранена без изменения; исходная формула в старых данных отсутствовала.';
                """);
        }
    }
}
