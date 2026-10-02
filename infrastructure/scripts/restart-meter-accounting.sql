-- Start a new accounting baseline while retaining earlier finance and reading history.
-- Require a verified backup; stop the API for the approved production execution.
-- psql -X -v database_name=... -v start_month=2026-10-01 -v reason='...'
-- -v execute=false (transactional preview) or execute=true.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SELECT set_config('garagebalance.restart_database', :'database_name', true),
       set_config('garagebalance.restart_month', :'start_month', true),
       set_config('garagebalance.restart_reason', :'reason', true);
DO $restart$
DECLARE
    start_month date := current_setting('garagebalance.restart_month')::date;
    reason text := current_setting('garagebalance.restart_reason');
BEGIN
    IF current_database() <> current_setting('garagebalance.restart_database')
       OR current_database() !~ '^garagebalance_(staging|it_[a-z0-9_]+)$' THEN
        RAISE EXCEPTION 'Unexpected GarageBalance restart database';
    END IF;
    IF start_month <> date_trunc('month', start_month)::date
       OR length(btrim(reason)) NOT BETWEEN 5 AND 500 THEN
        RAISE EXCEPTION 'A month boundary and meaningful reason are required';
    END IF;
    LOCK TABLE garages, meter_devices, meter_readings, accruals,
        accrual_payment_allocations, financial_operations, charge_service_settings
        IN SHARE ROW EXCLUSIVE MODE;
    -- Never silently change a paid amount or discard already entered later months.
    IF EXISTS (SELECT 1 FROM accruals a JOIN accrual_payment_allocations p ON p."AccrualId" = a."Id"
               WHERE NOT a."IsCanceled" AND a."AccountingMonth" >= start_month
                 AND (a."RequiresMeterReading" OR a."CalculationMeterKind" IS NOT NULL) AND p."IsActive") THEN
        RAISE EXCEPTION 'Paid meter accruals in the restart period prevent restart';
    END IF;
    IF EXISTS (SELECT 1 FROM meter_readings WHERE NOT "IsCanceled" AND "AccountingMonth" > start_month)
       OR EXISTS (SELECT 1 FROM meter_devices WHERE "RemovedOn" IS NULL AND "InstalledOn" >= start_month
                  AND "SerialNumber" <> 'Начало учёта ' || to_char(start_month, 'DD.MM.YYYY')) THEN
        RAISE EXCEPTION 'Later readings or newly installed devices require individual review';
    END IF;
    IF EXISTS (SELECT 1 FROM audit_events WHERE "Action" = 'finance.meter_accounting_restarted'
               AND "EntityId" = start_month::text) THEN
        RAISE NOTICE 'Baseline already restarted; no changes';
        RETURN;
    END IF;

    INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "Summary",
        "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
    SELECT gen_random_uuid(), now(), 'finance.meter_reading_archived_for_restart', 'meter_reading', "Id"::text,
        'Показание перенесено в историю при начале нового учёта',
        jsonb_build_object('reason', reason, 'startMonth', start_month, 'oldValues', to_jsonb(r))::text,
        'Показание счётчика', 'archive', 'finance'
    FROM meter_readings r WHERE NOT "IsCanceled";
    UPDATE meter_readings SET "IsCanceled" = true, "Version" = gen_random_uuid(), "UpdatedAtUtc" = now()
    WHERE NOT "IsCanceled";

    INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "Summary",
        "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
    SELECT gen_random_uuid(), now(), 'finance.accrual_canceled_for_meter_restart', 'accrual', "Id"::text,
        'Неоплаченное начисление отменено перед новым вводом показаний',
        jsonb_build_object('reason', reason, 'startMonth', start_month, 'oldValues', to_jsonb(a))::text,
        'Начисление по счётчику', 'cancel', 'finance'
    FROM accruals a WHERE NOT "IsCanceled" AND "AccountingMonth" >= start_month
        AND ("RequiresMeterReading" OR "CalculationMeterKind" IS NOT NULL);
    UPDATE accruals SET "IsCanceled" = true, "UpdatedAtUtc" = now()
    WHERE NOT "IsCanceled" AND "AccountingMonth" >= start_month
        AND ("RequiresMeterReading" OR "CalculationMeterKind" IS NOT NULL);

    INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "Summary",
        "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
    SELECT gen_random_uuid(), now(), 'finance.meter_baseline_restarted', 'garage', "Id"::text,
        'Начальные показания обнулены для нового учёта',
        jsonb_build_object('reason', reason, 'startMonth', start_month, 'oldValues', jsonb_build_object(
            'water', "InitialWaterMeterValue", 'electricity', "InitialElectricityMeterValue",
            'month', "InitialMeterReadingMonth"))::text,
        'Гараж', 'update', 'finance' FROM garages WHERE NOT "IsArchived";
    UPDATE garages SET "InitialWaterMeterValue" = 0, "InitialElectricityMeterValue" = 0,
        "InitialMeterReadingMonth" = (start_month - interval '1 month')::date,
        "Version" = gen_random_uuid(), "UpdatedAtUtc" = now() WHERE NOT "IsArchived";

    -- These are logical accounting boundaries, not claims of a physical replacement.
    UPDATE meter_devices SET "RemovedOn" = start_month - 1,
        "FinalValue" = COALESCE((SELECT r."CurrentValue" FROM meter_readings r
            WHERE r."MeterDeviceId" = meter_devices."Id" AND r."AccountingMonth" < start_month
            ORDER BY r."ReadingDate" DESC LIMIT 1), "InitialValue"),
        "Version" = gen_random_uuid(), "UpdatedAtUtc" = now() WHERE "RemovedOn" IS NULL;
    INSERT INTO meter_devices ("Id", "GarageId", "MeterKind", "SerialNumber", "InstalledOn", "InitialValue",
        "Version", "CreatedAtUtc", "UpdatedAtUtc")
    SELECT gen_random_uuid(), g."Id", k.kind, 'Начало учёта ' || to_char(start_month, 'DD.MM.YYYY'),
        start_month, 0, gen_random_uuid(), now(), now()
    FROM garages g CROSS JOIN (
        SELECT 'electricity' AS kind UNION SELECT 'water'
        UNION SELECT "MeterKind" FROM charge_service_settings WHERE NOT "IsArchived" AND "IsMetered"
            AND "MeterKind" IS NOT NULL
    ) k WHERE NOT g."IsArchived";
    INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "Summary",
        "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
    VALUES (gen_random_uuid(), now(), 'finance.meter_accounting_restarted', 'meter_accounting', start_month::text,
        'Начат новый учёт показаний с нулевой базой; прежние начисления и оплаты сохранены',
        jsonb_build_object('reason', reason, 'startMonth', start_month, 'maintenance', true)::text,
        'Учёт показаний', 'update', 'finance');
END $restart$;
\if :execute
COMMIT;
\else
ROLLBACK;
\endif
