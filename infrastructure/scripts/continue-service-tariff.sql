-- Continue the last fixed/person tariff from an explicit month, retaining old financial references.
-- psql -X -v database_name=... -v service_id=... -v start_month=2026-10-01
-- -v rate=125 -v archive_before=2026-09-01 -v reason='...' -v execute=false|true
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SELECT set_config('garagebalance.tariff_database', :'database_name', true),
       set_config('garagebalance.tariff_service', :'service_id', true),
       set_config('garagebalance.tariff_month', :'start_month', true),
       set_config('garagebalance.tariff_rate', :'rate', true),
       set_config('garagebalance.tariff_archive_before', :'archive_before', true),
       set_config('garagebalance.tariff_reason', :'reason', true);
DO $continue$
DECLARE
    service_id uuid := current_setting('garagebalance.tariff_service')::uuid;
    start_month date := current_setting('garagebalance.tariff_month')::date;
    archive_before date := current_setting('garagebalance.tariff_archive_before')::date;
    rate numeric := current_setting('garagebalance.tariff_rate')::numeric;
    reason text := current_setting('garagebalance.tariff_reason');
    previous_tariff uuid;
    new_tariff uuid := gen_random_uuid();
BEGIN
    IF current_database() <> current_setting('garagebalance.tariff_database')
       OR current_database() !~ '^garagebalance_(staging|it_[a-z0-9_]+)$' THEN
        RAISE EXCEPTION 'Unexpected GarageBalance tariff database';
    END IF;
    IF start_month <> date_trunc('month', start_month)::date OR archive_before > start_month
       OR rate <= 0 OR rate >= 1000000000 OR length(btrim(reason)) NOT BETWEEN 5 AND 500 THEN
        RAISE EXCEPTION 'Invalid tariff continuation parameters';
    END IF;
    LOCK TABLE charge_service_settings, charge_service_tariff_versions, tariffs IN SHARE ROW EXCLUSIVE MODE;
    IF NOT EXISTS (SELECT 1 FROM charge_service_settings WHERE "Id" = service_id AND NOT "IsArchived"
                   AND "IsRegular" AND NOT "IsMetered" AND "PeriodicityMonths" = 1) THEN
        RAISE EXCEPTION 'Expected an active monthly nonmetered service';
    END IF;
    IF EXISTS (SELECT 1 FROM charge_service_tariff_versions v JOIN tariffs t ON t."Id" = v."TariffId"
               WHERE v."ChargeServiceSettingId" = service_id AND NOT v."IsArchived"
                 AND v."EffectiveFrom" = start_month AND v."EffectiveTo" IS NULL AND t."Rate" = rate) THEN
        RAISE NOTICE 'Matching tariff continuation already exists';
    ELSE
        IF EXISTS (SELECT 1 FROM charge_service_tariff_versions WHERE "ChargeServiceSettingId" = service_id
                   AND NOT "IsArchived" AND ("EffectiveTo" IS NULL OR "EffectiveTo" >= start_month)) THEN
            RAISE EXCEPTION 'An overlapping tariff period requires individual review';
        END IF;
        SELECT v."TariffId" INTO previous_tariff FROM charge_service_tariff_versions v JOIN tariffs t ON t."Id" = v."TariffId"
        WHERE v."ChargeServiceSettingId" = service_id AND NOT v."IsArchived" AND v."EffectiveTo" = start_month - 1
          AND t."CalculationBase" IN ('fixed', 'people') AND NOT t."IsArchived"
        ORDER BY v."EffectiveFrom" DESC LIMIT 1;
        IF previous_tariff IS NULL THEN RAISE EXCEPTION 'Expected a fixed/person period ending immediately before restart'; END IF;
        INSERT INTO tariffs SELECT (jsonb_populate_record(NULL::tariffs, to_jsonb(t) || jsonb_build_object(
            'Id', new_tariff, 'Rate', rate, 'EffectiveFrom', start_month, 'Version', gen_random_uuid(),
            'CreatedAtUtc', now(), 'UpdatedAtUtc', now()))).* FROM tariffs t WHERE "Id" = previous_tariff;
        INSERT INTO charge_service_tariff_versions ("ChargeServiceSettingId", "EffectiveFrom", "EffectiveTo", "IsArchived", "TariffId", "CreatedAtUtc")
        VALUES (service_id, start_month, NULL, false, new_tariff, now());
        UPDATE charge_service_settings SET "TariffId" = new_tariff, "Version" = gen_random_uuid(), "UpdatedAtUtc" = now() WHERE "Id" = service_id;
        INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "Summary",
            "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
        VALUES (gen_random_uuid(), now(), 'dictionary.charge_service_tariff_continued', 'charge_service', service_id::text,
            'Добавлен следующий период тарифа услуги', jsonb_build_object('reason', reason, 'effectiveFrom', start_month,
            'rate', rate, 'previousTariffId', previous_tariff, 'newTariffId', new_tariff)::text, 'Тариф услуги', 'update', 'dictionary');
    END IF;
    INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "Summary",
        "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
    SELECT gen_random_uuid(), now(), 'dictionary.charge_service_tariff_period_archived', 'charge_service', v."ChargeServiceSettingId"::text,
        'Исторический период тарифа перенесён в архив; начисления сохранены',
        jsonb_build_object('reason', reason, 'oldValues', to_jsonb(v), 'archiveBefore', archive_before)::text,
        'Период тарифа', 'archive', 'dictionary' FROM charge_service_tariff_versions v
    WHERE NOT v."IsArchived" AND v."EffectiveTo" < archive_before;
    UPDATE charge_service_tariff_versions SET "IsArchived" = true WHERE NOT "IsArchived" AND "EffectiveTo" < archive_before;
END $continue$;
\if :execute
COMMIT;
\else
ROLLBACK;
\endif
