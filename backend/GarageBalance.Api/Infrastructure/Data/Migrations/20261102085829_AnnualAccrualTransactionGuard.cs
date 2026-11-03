using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

public partial class AnnualAccrualTransactionGuard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO public.application_settings ("Id", "Key", "BooleanValue", "JsonValue", "UpdatedAtUtc", "Version")
            SELECT 'f6a605a9-1ab2-444d-b3d9-18ff2c73a791'::uuid, 'finance.annual_accrual_guard_epoch', false,
                   COALESCE(jsonb_object_agg(substring("Key" from '[0-9]{4}$'), 'true'::jsonb)
                       FILTER (WHERE "BooleanValue" AND "Key" ~ '^finance[.]annual_accrual_hold[.][0-9]{4}$'), '{}'::jsonb),
                   now(), '2a8bbcd1-12ee-4be2-8ae7-ab99c1e1f979'::uuid
            FROM public.application_settings;

            CREATE FUNCTION public.gb_guard_policy_consistency() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            DECLARE actual jsonb; expected jsonb;
            BEGIN
                SELECT "JsonValue" INTO actual FROM public.application_settings
                    WHERE "Key"='finance.annual_accrual_guard_epoch';
                IF NOT FOUND OR actual IS NULL OR jsonb_typeof(actual) <> 'object' THEN
                    RAISE EXCEPTION 'annual guard epoch missing or corrupt' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_invalid';
                END IF;
                SELECT COALESCE(jsonb_object_agg(substring("Key" from '[0-9]{4}$'), 'true'::jsonb)
                    FILTER (WHERE "BooleanValue" AND "Key" ~ '^finance[.]annual_accrual_hold[.][0-9]{4}$'), '{}'::jsonb)
                    INTO expected FROM public.application_settings;
                IF actual IS DISTINCT FROM expected THEN
                    RAISE EXCEPTION 'annual guard epoch differs from policies' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_invalid';
                END IF;
                RETURN NULL;
            END $$;
            CREATE CONSTRAINT TRIGGER gb_annual_policy_consistency
                AFTER INSERT OR UPDATE OR DELETE ON public.application_settings DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION public.gb_guard_policy_consistency();

            CREATE FUNCTION public.gb_accrual_is_annual(
                accounting_year integer, accounting_month date, income_id uuid, source text, fee_id uuid,
                irregular_id uuid, basis text, tariff_id uuid, checking_old boolean) RETURNS boolean
            LANGUAGE plpgsql STABLE SET search_path = pg_catalog AS $$
            DECLARE code text; active_count integer; annual_count integer; monthly_count integer; legacy boolean;
            BEGIN
                IF accounting_year IS NOT NULL THEN RETURN true; END IF;
                IF irregular_id IS NOT NULL AND source <> 'manual' THEN
                    RAISE EXCEPTION 'annual guard inconsistent irregular source' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                SELECT lower(trim("Code")) INTO code FROM public.income_types WHERE "Id"=income_id;
                IF NOT FOUND THEN RAISE EXCEPTION 'annual guard income type missing' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_invalid'; END IF;
                IF source = 'manual' AND fee_id IS NULL AND (irregular_id IS NOT NULL OR basis IS NOT NULL) THEN
                    IF code = 'other_payments' THEN RETURN false; END IF;
                    RAISE EXCEPTION 'annual guard inconsistent irregular income' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                IF source = 'debt_transfer' OR code = 'debt_transfer' THEN
                    IF source = 'debt_transfer' AND code = 'debt_transfer' THEN RETURN false; END IF;
                    RAISE EXCEPTION 'annual guard inconsistent debt transfer' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                IF code = 'penalty' AND source = 'manual' THEN RETURN false; END IF;
                legacy := COALESCE(code IN ('membership','target','outdoor_lighting'), false);
                SELECT count(*), count(*) FILTER (WHERE COALESCE(s."PeriodicityMonths" >= 12, legacy)),
                       count(*) FILTER (WHERE NOT COALESCE(s."PeriodicityMonths" >= 12, legacy))
                INTO active_count, annual_count, monthly_count
                FROM public.charge_service_settings s
                WHERE NOT s."IsArchived" AND s."IsRegular" AND s."IncomeTypeId"=income_id
                  AND (tariff_id IS NULL OR s."TariffId"=tariff_id OR EXISTS
                      (SELECT 1 FROM public.charge_service_tariff_versions v
                       WHERE v."ChargeServiceSettingId"=s."Id" AND v."TariffId"=tariff_id AND NOT v."IsArchived"));
                IF active_count = 0 THEN
                    -- Legacy rows can refer to an archived service or tariff version.
                    SELECT count(*), count(*) FILTER (WHERE COALESCE(s."PeriodicityMonths" >= 12, legacy)),
                           count(*) FILTER (WHERE NOT COALESCE(s."PeriodicityMonths" >= 12, legacy))
                    INTO active_count, annual_count, monthly_count
                    FROM public.charge_service_settings s
                    WHERE s."IsRegular" AND s."IncomeTypeId"=income_id
                      AND (tariff_id IS NULL OR s."TariffId"=tariff_id OR EXISTS
                          (SELECT 1 FROM public.charge_service_tariff_versions v
                           WHERE v."ChargeServiceSettingId"=s."Id" AND v."TariffId"=tariff_id));
                END IF;
                IF annual_count > 0 AND monthly_count > 0 THEN
                    -- A null-year legacy row has no reliable discriminator between these services.
                    RAISE EXCEPTION 'annual guard ambiguous income type' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                IF active_count > 0 THEN RETURN annual_count > 0; END IF;
                IF legacy THEN RETURN true; END IF;
                IF code IN ('other_payments','other_income') THEN RETURN false; END IF;
                RAISE EXCEPTION 'annual guard cannot classify income type' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
            END $$;

            CREATE FUNCTION public.gb_guard_annual_accrual() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            DECLARE held jsonb; expected jsonb; candidate_year integer; check_old boolean;
            BEGIN
                SELECT "JsonValue" INTO held FROM public.application_settings
                WHERE "Key"='finance.annual_accrual_guard_epoch' FOR SHARE;
                IF NOT FOUND OR held IS NULL OR jsonb_typeof(held) <> 'object' THEN
                    RAISE EXCEPTION 'annual guard epoch missing or corrupt' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_invalid';
                END IF;
                IF EXISTS (SELECT 1 FROM jsonb_each(held) AS item WHERE item.key !~ '^[0-9]{4}$' OR item.value <> 'true'::jsonb) THEN
                    RAISE EXCEPTION 'annual guard epoch missing or corrupt' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_invalid';
                END IF;
                SELECT COALESCE(jsonb_object_agg(substring("Key" from '[0-9]{4}$'), 'true'::jsonb)
                    FILTER (WHERE "BooleanValue" AND "Key" ~ '^finance[.]annual_accrual_hold[.][0-9]{4}$'), '{}'::jsonb)
                    INTO expected FROM public.application_settings;
                IF held IS DISTINCT FROM expected THEN
                    RAISE EXCEPTION 'annual guard epoch differs from policies' USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_invalid';
                END IF;
                IF held = '{}'::jsonb THEN RETURN NEW; END IF;
                IF TG_OP='UPDATE' THEN
                    candidate_year := COALESCE(OLD."AccountingYear", extract(year FROM OLD."AccountingMonth")::integer);
                    check_old := held ? candidate_year::text OR held ? extract(year FROM OLD."AccountingMonth")::integer::text;
                    IF check_old AND public.gb_accrual_is_annual(OLD."AccountingYear", OLD."AccountingMonth", OLD."IncomeTypeId", OLD."Source",
                        OLD."FeeCampaignId", OLD."IrregularPaymentId", OLD."Basis", OLD."TariffId", true) THEN
                        -- Allow only a cancellation, never a rewrite into a different year/type/value.
                        IF NOT (NEW."IsCanceled" AND NOT OLD."IsCanceled" AND
                            (to_jsonb(NEW) - ARRAY['IsCanceled','Comment','UpdatedAtUtc']) =
                            (to_jsonb(OLD) - ARRAY['IsCanceled','Comment','UpdatedAtUtc'])) THEN
                            RAISE EXCEPTION 'annual accrual held' USING ERRCODE='23514', CONSTRAINT='gb_annual_accrual_held';
                        END IF;
                    END IF;
                END IF;
                IF NOT NEW."IsCanceled" THEN
                    candidate_year := COALESCE(NEW."AccountingYear", extract(year FROM NEW."AccountingMonth")::integer);
                    IF (held ? candidate_year::text OR held ? extract(year FROM NEW."AccountingMonth")::integer::text) AND public.gb_accrual_is_annual(NEW."AccountingYear", NEW."AccountingMonth", NEW."IncomeTypeId", NEW."Source",
                        NEW."FeeCampaignId", NEW."IrregularPaymentId", NEW."Basis", NEW."TariffId", false) THEN
                        RAISE EXCEPTION 'annual accrual held' USING ERRCODE='23514', CONSTRAINT='gb_annual_accrual_held';
                    END IF;
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER gb_annual_accrual_guard BEFORE INSERT OR UPDATE ON public.accruals
                FOR EACH ROW EXECUTE FUNCTION public.gb_guard_annual_accrual();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF (SELECT count(*) FROM public.application_settings WHERE "Key"='finance.annual_accrual_guard_epoch' AND "JsonValue" = '{}'::jsonb) <> 1
                   OR EXISTS (SELECT 1 FROM public.application_settings WHERE "Key" ~ '^finance[.]annual_accrual_hold[.][0-9]{4}$' AND "BooleanValue") THEN
                    RAISE EXCEPTION 'Active annual holds must be released before removing their guard.';
                END IF;
            END $$;
            DROP TRIGGER gb_annual_policy_consistency ON public.application_settings;
            DROP FUNCTION public.gb_guard_policy_consistency();
            DROP TRIGGER gb_annual_accrual_guard ON public.accruals;
            DROP FUNCTION public.gb_guard_annual_accrual();
            DROP FUNCTION public.gb_accrual_is_annual(integer,date,uuid,text,uuid,uuid,text,uuid,boolean);
            DELETE FROM public.application_settings WHERE "Key"='finance.annual_accrual_guard_epoch';
            """);
    }
}
