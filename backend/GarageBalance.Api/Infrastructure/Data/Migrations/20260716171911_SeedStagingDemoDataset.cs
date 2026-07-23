using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedStagingDemoDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $demo$
                DECLARE
                    demo_audit_id uuid := md5('garagebalance-staging-demo-dataset-v1')::uuid;
                BEGIN
                    IF lower(COALESCE(current_setting('garagebalance.demo_seed_enabled', true), 'off')) <> 'on' THEN
                        RETURN;
                    END IF;

                    IF EXISTS (SELECT 1 FROM audit_events WHERE "Id" = demo_audit_id) THEN
                        RETURN;
                    END IF;

                    INSERT INTO owners (
                        "Id", "LastName", "FirstName", "MiddleName", "Phone", "Address", "MeterNotes",
                        "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-owner-' || source.owner_no)::uuid,
                        source.last_name,
                        source.first_name,
                        source.middle_name,
                        format('+7 (900) 000-%s-%s', lpad(source.owner_no::text, 2, '0'), lpad((source.owner_no * 3)::text, 2, '0')),
                        format('Новосибирская область, г. Искитим, ул. Демонстрационная, д. %s, кв. %s', 10 + source.owner_no, source.owner_no),
                        'Демонстрационный владелец. Показания счётчиков внесены за январь 2021 — июль 2026.',
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z' + make_interval(days => source.owner_no),
                        TIMESTAMPTZ '2026-07-17T00:00:00Z'
                    FROM (VALUES
                        (1, 'Иванов', 'Иван', 'Иванович'),
                        (2, 'Петров', 'Сергей', 'Александрович'),
                        (3, 'Сидорова', 'Елена', 'Викторовна'),
                        (4, 'Смирнов', 'Алексей', 'Николаевич'),
                        (5, 'Кузнецова', 'Ольга', 'Петровна'),
                        (6, 'Попов', 'Дмитрий', 'Сергеевич'),
                        (7, 'Васильева', 'Наталья', 'Андреевна'),
                        (8, 'Соколов', 'Михаил', 'Юрьевич'),
                        (9, 'Михайлова', 'Татьяна', 'Олеговна'),
                        (10, 'Новиков', 'Андрей', 'Владимирович'),
                        (11, 'Фёдорова', 'Ирина', 'Михайловна'),
                        (12, 'Морозов', 'Виктор', 'Павлович'),
                        (13, 'Волкова', 'Марина', 'Сергеевна'),
                        (14, 'Алексеев', 'Роман', 'Игоревич'),
                        (15, 'Лебедева', 'Светлана', 'Анатольевна'),
                        (16, 'Семёнов', 'Николай', 'Васильевич'),
                        (17, 'Егорова', 'Людмила', 'Александровна'),
                        (18, 'Павлов', 'Константин', 'Романович'),
                        (19, 'Козлова', 'Анна', 'Дмитриевна'),
                        (20, 'Степанов', 'Евгений', 'Валерьевич')
                    ) AS source(owner_no, last_name, first_name, middle_name)
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO garages (
                        "Id", "Number", "PeopleCount", "FloorCount", "StartingBalance",
                        "InitialWaterMeterValue", "InitialElectricityMeterValue", "Comment", "IsArchived",
                        "CreatedAtUtc", "UpdatedAtUtc", "OwnerId")
                    SELECT
                        md5('garagebalance-demo-garage-' || source.garage_no)::uuid,
                        source.garage_no::text,
                        1 + (source.garage_no % 4),
                        1 + (source.garage_no % 3),
                        CASE WHEN source.garage_no % 6 = 0 THEN 1500.00 ELSE 0.00 END,
                        90 + source.garage_no * 7,
                        1500 + source.garage_no * 110,
                        'Демонстрационный гараж для проверки начислений, оплат, долгов, счётчиков и отчётов.',
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z' + make_interval(days => source.garage_no - 100),
                        TIMESTAMPTZ '2026-07-17T00:00:00Z',
                        md5('garagebalance-demo-owner-' || (source.garage_no - 100))::uuid
                    FROM generate_series(101, 120) AS source(garage_no)
                    ON CONFLICT DO NOTHING;

                    INSERT INTO supplier_groups (
                        "Id", "Name", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-supplier-group-' || source.group_no)::uuid,
                        source.group_name,
                        FALSE,
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z',
                        TIMESTAMPTZ '2026-07-17T00:00:00Z'
                    FROM (VALUES
                        (1, 'Электроснабжение'),
                        (2, 'Водоснабжение'),
                        (3, 'Вывоз отходов'),
                        (4, 'Охрана территории'),
                        (5, 'Связь и обслуживание')
                    ) AS source(group_no, group_name)
                    ON CONFLICT DO NOTHING;

                    INSERT INTO suppliers (
                        "Id", "Name", "Inn", "LegalAddress", "ContactPerson", "Phone", "Email",
                        "StartingBalance", "Comment", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc",
                        "GroupId", "ChargeServiceSettingId")
                    SELECT
                        md5('garagebalance-demo-supplier-' || source.supplier_no)::uuid,
                        source.supplier_name,
                        source.inn,
                        format('Новосибирская область, г. Искитим, ул. Промышленная, д. %s', 20 + source.supplier_no),
                        source.contact_name,
                        format('+7 (383) 200-10-%s', lpad(source.supplier_no::text, 2, '0')),
                        format('demo-supplier-%s@example.test', source.supplier_no),
                        source.starting_balance,
                        'Демонстрационный поставщик. Реквизиты и контакты вымышлены.',
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z',
                        TIMESTAMPTZ '2026-07-17T00:00:00Z',
                        md5('garagebalance-demo-supplier-group-' || source.supplier_no)::uuid,
                        source.service_id
                    FROM (VALUES
                        (1, 'ООО «СибирьЭнергоСервис»', '5400000001', 'Орлов Павел Максимович', 18500.00, 'f0d7ed2e-ec55-42b4-8a79-01b37c287102'::uuid),
                        (2, 'МУП «Горводоканал-Демо»', '5400000002', 'Беляева Алёна Игоревна', 9200.00, 'f0d7ed2e-ec55-42b4-8a79-01b37c287101'::uuid),
                        (3, 'ООО «Чистый город»', '5400000003', 'Комаров Денис Олегович', 4800.00, 'f0d7ed2e-ec55-42b4-8a79-01b37c287105'::uuid),
                        (4, 'ООО «Рубеж-Охрана»', '5400000004', 'Тихонов Артём Сергеевич', 7000.00, 'f0d7ed2e-ec55-42b4-8a79-01b37c287106'::uuid),
                        (5, 'АО «РегионСвязь-Сервис»', '5400000005', 'Захарова Полина Андреевна', 3500.00, 'f0d7ed2e-ec55-42b4-8a79-01b37c287104'::uuid)
                    ) AS source(supplier_no, supplier_name, inn, contact_name, starting_balance, service_id)
                    ON CONFLICT DO NOTHING;

                    INSERT INTO supplier_contacts (
                        "Id", "SupplierId", "FullName", "Position", "Phone", "Email", "Status", "Comment",
                        "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-supplier-contact-' || source.supplier_no)::uuid,
                        md5('garagebalance-demo-supplier-' || source.supplier_no)::uuid,
                        source.contact_name,
                        source.position_name,
                        format('+7 (913) 000-20-%s', lpad(source.supplier_no::text, 2, '0')),
                        format('demo-contact-%s@example.test', source.supplier_no),
                        'Работает',
                        'Основной демонстрационный контакт поставщика.',
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z',
                        TIMESTAMPTZ '2026-07-17T00:00:00Z'
                    FROM (VALUES
                        (1, 'Орлов Павел Максимович', 'Менеджер по работе с клиентами'),
                        (2, 'Беляева Алёна Игоревна', 'Специалист абонентского отдела'),
                        (3, 'Комаров Денис Олегович', 'Руководитель участка'),
                        (4, 'Тихонов Артём Сергеевич', 'Начальник смены'),
                        (5, 'Захарова Полина Андреевна', 'Сервисный менеджер')
                    ) AS source(supplier_no, contact_name, position_name)
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO staff_departments (
                        "Id", "Name", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-department-' || source.department_no)::uuid,
                        source.department_name,
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z',
                        TIMESTAMPTZ '2026-07-17T00:00:00Z'
                    FROM (VALUES
                        (1, 'Администрация ГСК'),
                        (2, 'Бухгалтерия'),
                        (3, 'Эксплуатация'),
                        (4, 'Охрана')
                    ) AS source(department_no, department_name)
                    ON CONFLICT DO NOTHING;

                    INSERT INTO staff_members (
                        "Id", "FullName", "Rate", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc", "DepartmentId")
                    SELECT
                        md5('garagebalance-demo-staff-' || source.staff_no)::uuid,
                        source.full_name,
                        source.rate,
                        FALSE,
                        TIMESTAMPTZ '2021-01-01T00:00:00Z',
                        TIMESTAMPTZ '2026-07-17T00:00:00Z',
                        md5('garagebalance-demo-department-' || source.department_no)::uuid
                    FROM (VALUES
                        (1, 'Петров Пётр Петрович', 45000.00, 1),
                        (2, 'Соколова Мария Андреевна', 42000.00, 2),
                        (3, 'Крылов Антон Викторович', 36000.00, 3),
                        (4, 'Громов Николай Сергеевич', 34000.00, 3),
                        (5, 'Романова Елена Павловна', 32000.00, 2),
                        (6, 'Никитин Олег Ильич', 30000.00, 4),
                        (7, 'Макаров Игорь Алексеевич', 30000.00, 4)
                    ) AS source(staff_no, full_name, rate, department_no)
                    ON CONFLICT ("Id") DO NOTHING;

                    WITH demo_garages AS (
                        SELECT
                            garage."Id" AS garage_id,
                            garage."Number"::integer - 100 AS garage_no,
                            garage."InitialWaterMeterValue" AS initial_water,
                            garage."InitialElectricityMeterValue" AS initial_electricity
                        FROM garages garage
                        WHERE garage."Id" IN (
                            SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                            FROM generate_series(101, 120) AS ids(garage_no))
                    ),
                    months AS (
                        SELECT
                            month_value::date AS accounting_month,
                            row_number() OVER (ORDER BY month_value)::integer AS month_no
                        FROM generate_series(DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS dates(month_value)
                    ),
                    consumption AS (
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            month.accounting_month,
                            month.month_no,
                            'water'::text AS meter_kind,
                            (2 + mod(garage.garage_no + month.month_no, 5))::numeric(18,3) AS consumption,
                            garage.initial_water AS initial_value
                        FROM demo_garages garage CROSS JOIN months month
                        UNION ALL
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            month.accounting_month,
                            month.month_no,
                            'electricity'::text,
                            (45 + garage.garage_no * 3 + mod(month.month_no, 12) * 4)::numeric(18,3),
                            garage.initial_electricity
                        FROM demo_garages garage CROSS JOIN months month
                    ),
                    readings AS (
                        SELECT
                            source.*,
                            source.initial_value + sum(source.consumption) OVER (
                                PARTITION BY source.garage_id, source.meter_kind
                                ORDER BY source.accounting_month) AS current_value
                        FROM consumption source
                    )
                    INSERT INTO meter_readings (
                        "Id", "GarageId", "MeterKind", "AccountingMonth", "ReadingDate", "CurrentValue",
                        "PreviousValue", "Consumption", "HasGapWarning", "Comment", "IsCanceled",
                        "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5(format('garagebalance-demo-meter-%s-%s-%s', reading.garage_no, reading.meter_kind, to_char(reading.accounting_month, 'YYYYMM')))::uuid,
                        reading.garage_id,
                        reading.meter_kind,
                        reading.accounting_month,
                        (reading.accounting_month + INTERVAL '24 days')::date,
                        reading.current_value,
                        reading.current_value - reading.consumption,
                        reading.consumption,
                        FALSE,
                        'Демонстрационное ежемесячное показание.',
                        FALSE,
                        (reading.accounting_month + INTERVAL '24 days')::timestamptz,
                        (reading.accounting_month + INTERVAL '24 days')::timestamptz
                    FROM readings reading
                    ON CONFLICT DO NOTHING;

                    WITH demo_garages AS (
                        SELECT
                            garage."Id" AS garage_id,
                            garage."Number"::integer - 100 AS garage_no,
                            garage."PeopleCount" AS people_count
                        FROM garages garage
                        WHERE garage."Id" IN (
                            SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                            FROM generate_series(101, 120) AS ids(garage_no))
                    ),
                    readings AS (
                        SELECT
                            meter."GarageId" AS garage_id,
                            meter."AccountingMonth" AS accounting_month,
                            meter."MeterKind" AS meter_kind,
                            meter."Consumption" AS consumption
                        FROM meter_readings meter
                        WHERE meter."GarageId" IN (SELECT garage_id FROM demo_garages)
                          AND meter."IsCanceled" = FALSE
                    ),
                    monthly_services AS (
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            reading.accounting_month,
                            income."Id" AS income_type_id,
                            '8a92bf70-9339-4bbc-8e5d-a05cda185101'::uuid AS tariff_id,
                            round(reading.consumption * 58.40, 2) AS amount,
                            'water'::text AS service_code
                        FROM demo_garages garage
                        INNER JOIN readings reading ON reading.garage_id = garage.garage_id AND reading.meter_kind = 'water'
                        INNER JOIN income_types income ON income."Code" = 'water' AND income."IsArchived" = FALSE
                        UNION ALL
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            reading.accounting_month,
                            income."Id",
                            '8a92bf70-9339-4bbc-8e5d-a05cda185102'::uuid,
                            round(reading.consumption * 6.05, 2),
                            'electricity'
                        FROM demo_garages garage
                        INNER JOIN readings reading ON reading.garage_id = garage.garage_id AND reading.meter_kind = 'electricity'
                        INNER JOIN income_types income ON income."Code" = 'electricity' AND income."IsArchived" = FALSE
                        UNION ALL
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            month.accounting_month,
                            income."Id",
                            '8a92bf70-9339-4bbc-8e5d-a05cda185105'::uuid,
                            (garage.people_count * 180.00)::numeric(18,2),
                            'trash'
                        FROM demo_garages garage
                        CROSS JOIN (
                            SELECT month_value::date AS accounting_month
                            FROM generate_series(DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS dates(month_value)
                        ) month
                        CROSS JOIN income_types income
                        WHERE income."Code" = 'trash' AND income."IsArchived" = FALSE
                    ),
                    annual_services AS (
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            make_date(year_value, 6, 1) AS accounting_month,
                            income."Id" AS income_type_id,
                            '8a92bf70-9339-4bbc-8e5d-a05cda185103'::uuid AS tariff_id,
                            1800.00::numeric(18,2) AS amount,
                            'membership'::text AS service_code
                        FROM demo_garages garage
                        CROSS JOIN generate_series(2021, 2026) AS years(year_value)
                        CROSS JOIN income_types income
                        WHERE income."Code" = 'membership' AND income."IsArchived" = FALSE
                        UNION ALL
                        SELECT
                            garage.garage_id,
                            garage.garage_no,
                            make_date(year_value, 3, 1),
                            income."Id",
                            '8a92bf70-9339-4bbc-8e5d-a05cda185104'::uuid,
                            (2400 + (year_value - 2021) * 200)::numeric(18,2),
                            'target'
                        FROM demo_garages garage
                        CROSS JOIN generate_series(2021, 2026) AS years(year_value)
                        CROSS JOIN income_types income
                        WHERE income."Code" = 'target' AND income."IsArchived" = FALSE
                    ),
                    all_services AS (
                        SELECT * FROM monthly_services
                        UNION ALL
                        SELECT * FROM annual_services
                    )
                    INSERT INTO accruals (
                        "Id", "GarageId", "IncomeTypeId", "TariffId", "AccountingMonth", "Amount", "Source",
                        "Comment", "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5(format('garagebalance-demo-accrual-%s-%s-%s', service.garage_no, service.service_code, to_char(service.accounting_month, 'YYYYMM')))::uuid,
                        service.garage_id,
                        service.income_type_id,
                        service.tariff_id,
                        service.accounting_month,
                        service.amount,
                        'regular',
                        'Демонстрационное начисление по регулярной услуге.',
                        FALSE,
                        (service.accounting_month + INTERVAL '1 day')::timestamptz,
                        (service.accounting_month + INTERVAL '1 day')::timestamptz
                    FROM all_services service
                    ON CONFLICT DO NOTHING;

                    INSERT INTO financial_operations (
                        "Id", "OperationKind", "OperationDate", "AccountingMonth", "Amount", "DocumentNumber",
                        "Comment", "GarageId", "IncomeTypeId", "SupplierId", "StaffMemberId", "ExpenseTypeId",
                        "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-income-' || accrual."Id")::uuid,
                        'income',
                        (accrual."AccountingMonth" + INTERVAL '20 days')::date,
                        accrual."AccountingMonth",
                        round(accrual."Amount" * (0.80 + mod(abs(hashtext(accrual."Id"::text)::bigint), 16) / 100.0), 2),
                        'ДОХ-' || to_char(accrual."AccountingMonth", 'YYYYMM') || '-' || right(garage."Number", 3) || '-' || left(income."Code", 3),
                        'Демонстрационная оплата начисления; часть сумм оставлена в долге для проверки отчётов.',
                        accrual."GarageId",
                        accrual."IncomeTypeId",
                        NULL,
                        NULL,
                        NULL,
                        FALSE,
                        (accrual."AccountingMonth" + INTERVAL '20 days')::timestamptz,
                        (accrual."AccountingMonth" + INTERVAL '20 days')::timestamptz
                    FROM accruals accrual
                    INNER JOIN garages garage ON garage."Id" = accrual."GarageId"
                    INNER JOIN income_types income ON income."Id" = accrual."IncomeTypeId"
                    WHERE accrual."GarageId" IN (
                        SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                        FROM generate_series(101, 120) AS ids(garage_no))
                      AND accrual."Comment" = 'Демонстрационное начисление по регулярной услуге.'
                      AND mod(abs(hashtext(accrual."Id"::text)::bigint), 9) <> 0
                    ON CONFLICT DO NOTHING;

                    WITH months AS (
                        SELECT
                            month_value::date AS accounting_month,
                            row_number() OVER (ORDER BY month_value)::integer AS month_no
                        FROM generate_series(DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS dates(month_value)
                    ),
                    supplier_rows AS (
                        SELECT
                            supplier_no,
                            md5('garagebalance-demo-supplier-' || supplier_no)::uuid AS supplier_id,
                            CASE supplier_no
                                WHEN 1 THEN 'ab93f9ed-4f83-40ef-b7b7-4e7ce8aaa000'::uuid
                                WHEN 2 THEN '9b2d316c-2503-42ad-acd5-9bf3199c5aae'::uuid
                                WHEN 3 THEN '18b03fd5-a9da-4ce6-9940-dd9848820d10'::uuid
                                WHEN 4 THEN '5ed33ff2-b69f-470c-8378-7fb312af5ed7'::uuid
                                ELSE '5c8451b8-78fe-412d-8def-123ba8ae47b3'::uuid
                            END AS expense_type_id,
                            CASE supplier_no
                                WHEN 1 THEN 27000.00
                                WHEN 2 THEN 14500.00
                                WHEN 3 THEN 9500.00
                                WHEN 4 THEN 12000.00
                                ELSE 4200.00
                            END::numeric(18,2) AS base_amount
                        FROM generate_series(1, 5) AS suppliers(supplier_no)
                    )
                    INSERT INTO supplier_accruals (
                        "Id", "SupplierId", "ExpenseTypeId", "AccountingMonth", "Amount", "Source",
                        "DocumentNumber", "Comment", "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5(format('garagebalance-demo-supplier-accrual-%s-%s', supplier.supplier_no, to_char(month.accounting_month, 'YYYYMM')))::uuid,
                        supplier.supplier_id,
                        supplier.expense_type_id,
                        month.accounting_month,
                        round(supplier.base_amount * (1 + mod(month.month_no + supplier.supplier_no, 7) / 100.0), 2),
                        'manual',
                        format('СЧ-ДЕМО-%s-%s', supplier.supplier_no, to_char(month.accounting_month, 'YYYYMM')),
                        'Демонстрационное ежемесячное обязательство перед поставщиком.',
                        FALSE,
                        (month.accounting_month + INTERVAL '5 days')::timestamptz,
                        (month.accounting_month + INTERVAL '5 days')::timestamptz
                    FROM supplier_rows supplier CROSS JOIN months month
                    ON CONFLICT DO NOTHING;

                    INSERT INTO financial_operations (
                        "Id", "OperationKind", "OperationDate", "AccountingMonth", "Amount", "DocumentNumber",
                        "Comment", "GarageId", "IncomeTypeId", "SupplierId", "StaffMemberId", "ExpenseTypeId",
                        "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-supplier-expense-' || accrual."Id")::uuid,
                        'expense',
                        (accrual."AccountingMonth" + INTERVAL '18 days')::date,
                        accrual."AccountingMonth",
                        round(accrual."Amount" * CASE WHEN mod(abs(hashtext(accrual."Id"::text)::bigint), 10) = 0 THEN 0.70 ELSE 0.96 END, 2),
                        'РАСХ-' || accrual."DocumentNumber",
                        'Демонстрационная выплата поставщику; отдельные счета оплачены частично.',
                        NULL,
                        NULL,
                        accrual."SupplierId",
                        NULL,
                        accrual."ExpenseTypeId",
                        FALSE,
                        (accrual."AccountingMonth" + INTERVAL '18 days')::timestamptz,
                        (accrual."AccountingMonth" + INTERVAL '18 days')::timestamptz
                    FROM supplier_accruals accrual
                    WHERE accrual."SupplierId" IN (
                        SELECT md5('garagebalance-demo-supplier-' || supplier_no)::uuid
                        FROM generate_series(1, 5) AS ids(supplier_no))
                      AND accrual."Comment" = 'Демонстрационное ежемесячное обязательство перед поставщиком.'
                    ON CONFLICT DO NOTHING;

                    WITH months AS (
                        SELECT month_value::date AS accounting_month
                        FROM generate_series(DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS dates(month_value)
                    )
                    INSERT INTO financial_operations (
                        "Id", "OperationKind", "OperationDate", "AccountingMonth", "Amount", "DocumentNumber",
                        "Comment", "GarageId", "IncomeTypeId", "SupplierId", "StaffMemberId", "ExpenseTypeId",
                        "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5(format('garagebalance-demo-salary-%s-%s', staff."Id", to_char(month.accounting_month, 'YYYYMM')))::uuid,
                        'expense',
                        (month.accounting_month + INTERVAL '27 days')::date,
                        month.accounting_month,
                        staff."Rate",
                        'ЗП-ДЕМО-' || to_char(month.accounting_month, 'YYYYMM') || '-' || right(staff."Id"::text, 4),
                        'Демонстрационная выплата заработной платы.',
                        NULL,
                        NULL,
                        NULL,
                        staff."Id",
                        '5ce3eb74-0605-4ae7-8b52-3da38c693aee'::uuid,
                        FALSE,
                        (month.accounting_month + INTERVAL '27 days')::timestamptz,
                        (month.accounting_month + INTERVAL '27 days')::timestamptz
                    FROM staff_members staff CROSS JOIN months month
                    WHERE staff."Id" IN (
                        SELECT md5('garagebalance-demo-staff-' || staff_no)::uuid
                        FROM generate_series(1, 7) AS ids(staff_no))
                    ON CONFLICT DO NOTHING;

                    INSERT INTO audit_events (
                        "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                        "EntityDisplayName", "Summary", "MetadataJson")
                    VALUES (
                        demo_audit_id,
                        TIMESTAMPTZ '2026-07-17T00:00:00Z',
                        'demo.dataset.seeded',
                        'settings',
                        'create',
                        'demo_dataset',
                        'staging-demo-v1',
                        'Демонстрационное наполнение CRM',
                        'Стенд наполнен связанными демонстрационными владельцами, гаражами, поставщиками, персоналом, показаниями, начислениями, поступлениями и выплатами.',
                        jsonb_build_object(
                            'owners', 20,
                            'garages', 20,
                            'suppliers', 5,
                            'staffMembers', 7,
                            'periodFrom', '2021-01',
                            'periodTo', '2026-07',
                            'containsRealPersonalData', false)::text)
                    ON CONFLICT ("Id") DO NOTHING;
                END $demo$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $demo$
                BEGIN
                    IF lower(COALESCE(current_setting('garagebalance.demo_seed_enabled', true), 'off')) <> 'on'
                       OR NOT EXISTS (
                           SELECT 1 FROM audit_events
                           WHERE "Id" = md5('garagebalance-staging-demo-dataset-v1')::uuid) THEN
                        RETURN;
                    END IF;

                    DELETE FROM financial_operations
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-income-' || "Id")::uuid
                        FROM accruals
                        WHERE "GarageId" IN (
                            SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                            FROM generate_series(101, 120) AS ids(garage_no)))
                       OR "Comment" IN (
                           'Демонстрационная выплата поставщику; отдельные счета оплачены частично.',
                           'Демонстрационная выплата заработной платы.');

                    DELETE FROM supplier_accruals
                    WHERE "SupplierId" IN (
                        SELECT md5('garagebalance-demo-supplier-' || supplier_no)::uuid
                        FROM generate_series(1, 5) AS ids(supplier_no));

                    DELETE FROM accruals
                    WHERE "GarageId" IN (
                        SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                        FROM generate_series(101, 120) AS ids(garage_no));

                    DELETE FROM meter_readings
                    WHERE "GarageId" IN (
                        SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                        FROM generate_series(101, 120) AS ids(garage_no));

                    DELETE FROM supplier_contacts
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-supplier-contact-' || supplier_no)::uuid
                        FROM generate_series(1, 5) AS ids(supplier_no));

                    DELETE FROM suppliers
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-supplier-' || supplier_no)::uuid
                        FROM generate_series(1, 5) AS ids(supplier_no));

                    DELETE FROM staff_members
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-staff-' || staff_no)::uuid
                        FROM generate_series(1, 7) AS ids(staff_no));

                    DELETE FROM staff_departments
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-department-' || department_no)::uuid
                        FROM generate_series(1, 4) AS ids(department_no));

                    DELETE FROM garages
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                        FROM generate_series(101, 120) AS ids(garage_no));

                    DELETE FROM owners
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-owner-' || owner_no)::uuid
                        FROM generate_series(1, 20) AS ids(owner_no));

                    DELETE FROM supplier_groups
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-supplier-group-' || group_no)::uuid
                        FROM generate_series(1, 5) AS ids(group_no));

                    DELETE FROM audit_events
                    WHERE "Id" = md5('garagebalance-staging-demo-dataset-v1')::uuid;
                END $demo$;
                """);
        }
    }
}
