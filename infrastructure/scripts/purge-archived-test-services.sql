-- Run with psql -X -v database_name=... -v service_ids=uuid,uuid
-- -v reason='...' -v execute=false (dry run) or execute=true (approved cleanup).
-- A checked pre-cleanup backup is mandatory. This script never deletes financial history.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SELECT set_config('garagebalance.cleanup_database', :'database_name', true),
       set_config('garagebalance.cleanup_ids', :'service_ids', true),
       set_config('garagebalance.cleanup_reason', :'reason', true),
       set_config('garagebalance.cleanup_execute', :'execute'::boolean::text, true);

DO $cleanup$
DECLARE
    target_ids uuid[];
    target_count integer;
    existing_count integer;
    cleanup_reason text := current_setting('garagebalance.cleanup_reason');
BEGIN
    IF current_database() <> current_setting('garagebalance.cleanup_database')
       OR current_database() !~ '^garagebalance_(staging|it_[a-z0-9_]+)$' THEN
        RAISE EXCEPTION 'Unexpected GarageBalance cleanup database';
    END IF;
    IF length(btrim(cleanup_reason)) NOT BETWEEN 5 AND 500 THEN
        RAISE EXCEPTION 'A meaningful cleanup reason is required';
    END IF;
    target_ids := string_to_array(current_setting('garagebalance.cleanup_ids'), ',')::uuid[];
    target_count := cardinality(target_ids);
    IF target_count NOT BETWEEN 1 AND 25
       OR target_count <> (SELECT count(DISTINCT item) FROM unnest(target_ids) item) THEN
        RAISE EXCEPTION 'Provide one to twenty-five distinct explicit service IDs';
    END IF;

    -- Prevent a concurrent writer from attaching records between validation and deletion.
    LOCK TABLE accruals, application_settings, charge_service_settings,
        charge_service_tariff_versions, fee_campaigns, financial_operations,
        garage_tariff_assignments, suppliers IN SHARE ROW EXCLUSIVE MODE;
    SELECT count(*) INTO existing_count FROM charge_service_settings WHERE "Id" = ANY(target_ids);
    IF existing_count = 0 THEN
        RAISE NOTICE 'Requested services are already absent; no changes';
        RETURN;
    END IF;
    IF existing_count <> target_count OR EXISTS (
        SELECT 1 FROM charge_service_settings WHERE "Id" = ANY(target_ids) AND NOT "IsArchived"
    ) THEN
        RAISE EXCEPTION 'Every requested service must exist and be archived';
    END IF;
    IF EXISTS (SELECT 1 FROM suppliers WHERE "ChargeServiceSettingId" = ANY(target_ids))
       OR EXISTS (SELECT 1 FROM garage_tariff_assignments WHERE "ChargeServiceSettingId" = ANY(target_ids))
       OR EXISTS (
           SELECT 1 FROM application_settings a, unnest(target_ids) target
           WHERE a."JsonValue"::text LIKE '%' || target::text || '%'
       ) THEN
        RAISE EXCEPTION 'Services still have operational/configuration references';
    END IF;
    IF EXISTS (
        SELECT 1 FROM charge_service_settings s
        WHERE s."Id" = ANY(target_ids) AND (
            EXISTS (SELECT 1 FROM accruals a WHERE a."IncomeTypeId" = s."IncomeTypeId"
                OR a."TariffId" = s."TariffId"
                OR a."TariffId" IN (SELECT "TariffId" FROM charge_service_tariff_versions WHERE "ChargeServiceSettingId" = s."Id"))
            OR EXISTS (SELECT 1 FROM financial_operations f WHERE f."IncomeTypeId" = s."IncomeTypeId")
            OR EXISTS (SELECT 1 FROM fee_campaigns f WHERE f."IncomeTypeId" = s."IncomeTypeId")
        )
    ) THEN
        RAISE EXCEPTION 'Financial history or collection references prevent cleanup';
    END IF;

    INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "EntityType", "EntityId",
        "Summary", "MetadataJson", "EntityDisplayName", "ActionKind", "Section")
    SELECT gen_random_uuid(), now(), 'dictionary.charge_service_test_purged', 'charge_service', "Id"::text,
        'Удалена архивная тестовая услуга: ' || "Name", jsonb_build_object(
            'reason', cleanup_reason, 'maintenance', true,
            'oldValues', jsonb_build_object('name', "Name", 'isArchived', "IsArchived"))::text,
        "Name", 'delete', 'dictionary'
    FROM charge_service_settings WHERE "Id" = ANY(target_ids);
    DELETE FROM charge_service_settings WHERE "Id" = ANY(target_ids);
    GET DIAGNOSTICS existing_count = ROW_COUNT;
    IF existing_count <> target_count THEN RAISE EXCEPTION 'Unexpected deletion count'; END IF;
    RAISE NOTICE 'Validated cleanup: % archived services; financial records retained', existing_count;
END $cleanup$;

\if :execute
COMMIT;
\else
ROLLBACK;
\endif
