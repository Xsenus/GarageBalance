using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistAccrualAnnualClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAnnualAccrual",
                table: "accruals",
                type: "boolean",
                nullable: true);

            // Preserve historical rows unchanged; AccountingYear itself remains authoritative.
            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.gb_guard_annual_accrual() RETURNS trigger
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
                -- A legacy row has no reliable historical provenance. A route change
                -- must not replace its unknown origin with a guessed classification.
                IF TG_OP='UPDATE' AND OLD."IsAnnualAccrual" IS NULL AND OLD."AccountingYear" IS NULL
                   AND (OLD."IncomeTypeId" IS DISTINCT FROM NEW."IncomeTypeId"
                        OR OLD."TariffId" IS DISTINCT FROM NEW."TariffId"
                        OR OLD."Source" IS DISTINCT FROM NEW."Source"
                        OR OLD."Basis" IS DISTINCT FROM NEW."Basis"
                        OR OLD."FeeCampaignId" IS DISTINCT FROM NEW."FeeCampaignId"
                        OR OLD."IrregularPaymentId" IS DISTINCT FROM NEW."IrregularPaymentId"
                        OR OLD."AccountingMonth" IS DISTINCT FROM NEW."AccountingMonth"
                        OR NEW."IsAnnualAccrual" IS NOT NULL OR NEW."AccountingYear" IS NOT NULL) THEN
                    RAISE EXCEPTION 'annual guard cannot reroute unknown historical classification'
                        USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                IF held = '{}'::jsonb THEN RETURN NEW; END IF;
                -- Unknown historical provenance cannot safely follow mutable dictionaries.
                IF TG_OP='UPDATE' AND OLD."IsAnnualAccrual" IS NULL AND OLD."AccountingYear" IS NULL
                   AND held ? extract(year FROM OLD."AccountingMonth")::integer::text THEN
                    RAISE EXCEPTION 'annual guard needs historical classification review'
                        USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                IF NOT NEW."IsCanceled" AND NEW."IsAnnualAccrual" IS NULL AND NEW."AccountingYear" IS NULL
                   AND held ? extract(year FROM NEW."AccountingMonth")::integer::text THEN
                    RAISE EXCEPTION 'annual guard needs historical classification review'
                        USING ERRCODE='23514', CONSTRAINT='gb_annual_guard_ambiguous';
                END IF;
                IF TG_OP='UPDATE' THEN
                    candidate_year := COALESCE(OLD."AccountingYear", extract(year FROM OLD."AccountingMonth")::integer);
                    check_old := held ? candidate_year::text OR held ? extract(year FROM OLD."AccountingMonth")::integer::text;
                    IF check_old AND (OLD."AccountingYear" IS NOT NULL OR COALESCE(OLD."IsAnnualAccrual", public.gb_accrual_is_annual(OLD."AccountingYear", OLD."AccountingMonth", OLD."IncomeTypeId", OLD."Source",
                        OLD."FeeCampaignId", OLD."IrregularPaymentId", OLD."Basis", OLD."TariffId", true))) THEN
                        -- Do not allow an automatic or stale cancellation to alter held history.
                        RAISE EXCEPTION 'annual accrual held' USING ERRCODE='23514', CONSTRAINT='gb_annual_accrual_held';
                    END IF;
                END IF;
                IF NOT NEW."IsCanceled" THEN
                    candidate_year := COALESCE(NEW."AccountingYear", extract(year FROM NEW."AccountingMonth")::integer);
                    IF (held ? candidate_year::text OR held ? extract(year FROM NEW."AccountingMonth")::integer::text) AND (NEW."AccountingYear" IS NOT NULL OR COALESCE(NEW."IsAnnualAccrual", public.gb_accrual_is_annual(NEW."AccountingYear", NEW."AccountingMonth", NEW."IncomeTypeId", NEW."Source",
                        NEW."FeeCampaignId", NEW."IrregularPaymentId", NEW."Basis", NEW."TariffId", false))) THEN
                        RAISE EXCEPTION 'annual accrual held' USING ERRCODE='23514', CONSTRAINT='gb_annual_accrual_held';
                    END IF;
                END IF;
                RETURN NEW;
            END $$;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM public.accruals WHERE "IsAnnualAccrual" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot discard persisted annual classification.';
                END IF;
            END $$;
            CREATE OR REPLACE FUNCTION public.gb_guard_annual_accrual() RETURNS trigger
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
            """);
            migrationBuilder.DropColumn(
                name: "IsAnnualAccrual",
                table: "accruals");
        }
    }
}
