using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileCleanCitySupplierService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $repair$
                DECLARE
                    trash_service_id uuid;
                    trash_expense_type_id uuid;
                    candidate record;
                BEGIN
                    SELECT service."Id" INTO trash_service_id
                    FROM supplier_services AS service
                    WHERE NOT service."IsArchived"
                      AND (service."Name" LIKE '%мусор%' OR service."Name" LIKE '%Мусор%'
                           OR service."Name" LIKE '%МУСОР%' OR service."Name" LIKE '%отход%'
                           OR service."Name" LIKE '%Отход%' OR service."Name" LIKE '%ОТХОД%')
                    ORDER BY CASE WHEN service."Name" IN ('мусор', 'Мусор', 'МУСОР') THEN 0 ELSE 1 END, service."Id"
                    LIMIT 1;

                    SELECT expense_type."Id" INTO trash_expense_type_id
                    FROM expense_types AS expense_type
                    WHERE NOT expense_type."IsArchived" AND expense_type."Code" = 'trash_removal'
                    ORDER BY expense_type."Id"
                    LIMIT 1;

                    IF trash_service_id IS NULL OR trash_expense_type_id IS NULL THEN
                        RETURN;
                    END IF;

                    FOR candidate IN
                        SELECT supplier."Id", supplier."Name", supplier."SupplierServiceId", supplier."ExpenseTypeId"
                        FROM suppliers AS supplier
                        INNER JOIN funds AS fund ON fund."Id" = supplier."ExpenseFundId"
                        LEFT JOIN supplier_services AS service ON service."Id" = supplier."SupplierServiceId"
                        LEFT JOIN expense_types AS expense_type ON expense_type."Id" = supplier."ExpenseTypeId"
                        WHERE NOT supplier."IsArchived"
                          AND (supplier."Name" LIKE '%чистый город%' OR supplier."Name" LIKE '%Чистый город%'
                               OR supplier."Name" LIKE '%ЧИСТЫЙ ГОРОД%')
                          AND (fund."Name" LIKE '%мусор%' OR fund."Name" LIKE '%Мусор%'
                               OR fund."Name" LIKE '%МУСОР%' OR fund."Name" LIKE '%отход%'
                               OR fund."Name" LIKE '%Отход%' OR fund."Name" LIKE '%ОТХОД%')
                          AND (supplier."SupplierServiceId" <> trash_service_id
                               OR supplier."ExpenseTypeId" IS DISTINCT FROM trash_expense_type_id)
                        FOR UPDATE OF supplier
                    LOOP
                        UPDATE suppliers
                        SET "SupplierServiceId" = trash_service_id,
                            "ExpenseTypeId" = trash_expense_type_id,
                            "UpdatedAtUtc" = NOW(),
                            "Version" = gen_random_uuid()
                        WHERE "Id" = candidate."Id";

                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            md5(candidate."Id"::text || ':clean-city-trash-link')::uuid,
                            NOW(), 'dictionary.supplier_service_link_reconciled', 'dictionaries', 'update', 'supplier',
                            candidate."Id"::text, candidate."Name",
                            'Связь услуги поставщика приведена в соответствие с мусорным фондом без изменения финансовой истории.',
                            jsonb_build_object(
                                'previousSupplierServiceId', candidate."SupplierServiceId",
                                'previousExpenseTypeId', candidate."ExpenseTypeId",
                                'supplierServiceId', trash_service_id,
                                'expenseTypeId', trash_expense_type_id,
                                'financialOperationsDeleted', 0)::text)
                        ON CONFLICT ("Id") DO NOTHING;
                    END LOOP;
                END $repair$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old links were inconsistent and cannot be restored safely after new
            // operations are recorded. Financial history itself is never rewritten.
        }
    }
}
