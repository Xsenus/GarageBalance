using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageBalance.StorageTool;

// A counterfactual for regular annual catalogue catch-up, not permission to generate.
// The normal finance service and its annual hold are never bypassed or invoked here.
public sealed class AnnualAccrualForecastEngine(GarageBalanceDbContext db,
    IBusinessDateProvider businessDate, TimeProvider clock)
{
    public const string Scope = "regular-annual-catalogue-catch-up-through-business-month-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AnnualAccrualForecast> PreviewAsync(int year, CancellationToken token)
    {
        if (year is < 1900 or > 9998) throw Error("annual_forecast_year_invalid");
        if (!db.Database.IsNpgsql()) throw Error("annual_forecast_postgresql_required");
        if (db.ChangeTracker.HasChanges()) throw Error("annual_forecast_pending_changes");
        var existing = db.Database.CurrentTransaction;
        if (existing is not null && existing.GetDbTransaction().IsolationLevel is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable))
            throw Error("annual_forecast_snapshot_required");

        // A release may own a SERIALIZABLE write transaction. Do not alter it.
        await using var owned = existing is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token) : null;
        if (owned is not null)
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        var result = await ReadAsync(year, token);
        if (owned is not null) await owned.RollbackAsync(token);
        return result;
    }

    private async Task<AnnualAccrualForecast> ReadAsync(int year, CancellationToken token)
    {
        var source = await AccountingCleanupSchema.ReadAsync(db, new Dictionary<string, string>(), token);
        var yearFrom = new DateOnly(year, 1, 1);
        var yearTo = new DateOnly(year, 12, 31);
        var key = AnnualAccrualHoldPolicy.Key(year);
        var policy = await db.ApplicationSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith(AnnualAccrualHoldPolicy.KeyPrefix) || s.Key == "finance.annual_accrual_guard_epoch" || s.Key == ApplicationSettingsService.BusinessDateOverrideKey)
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.BooleanValue, s.IntegerValue, s.DateValue, s.JsonValue, s.Version }).ToListAsync(token);
        var holdActive = policy.SingleOrDefault(s => s.Key == key)?.BooleanValue == true;
        // The stored override is read in this transaction, not by the application
        // initializer (which may write), and never mutates the shared provider.
        var today = policy.SingleOrDefault(s => s.Key == ApplicationSettingsService.BusinessDateOverrideKey)?.DateValue ?? businessDate.SystemDate;
        var monthNow = new DateOnly(today.Year, today.Month, 1);
        var garages = await db.Garages.AsNoTracking().Where(g => !g.IsArchived).OrderBy(g => g.Id)
            .Select(g => new GarageInput(g.Id, g.PeopleCount, g.FloorCount, g.AccountingStartMonth, g.RegisteredOn, g.CreatedAtUtc)).ToListAsync(token);
        var people = await db.GaragePeopleCountPeriods.AsNoTracking().Where(p => p.EffectiveFrom <= yearTo)
            .OrderBy(p => p.GarageId).ThenBy(p => p.EffectiveFrom)
            .Select(p => new { p.GarageId, p.EffectiveFrom, p.PeopleCount }).ToListAsync(token);
        var assignments = await db.GarageTariffAssignments.AsNoTracking()
            .Where(a => !a.IsArchived && a.EffectiveFrom <= yearTo && (a.EffectiveTo == null || a.EffectiveTo >= yearFrom))
            .OrderBy(a => a.Id).Select(a => new { a.Id, a.GarageId, a.ChargeServiceSettingId, a.EffectiveFrom, a.EffectiveTo, a.TariffId, a.Version }).ToListAsync(token);
        var accruals = await db.Accruals.AsNoTracking()
            .Where(a => a.Source == AccrualSources.Regular && (a.AccountingYear == year || a.AccountingMonth >= yearFrom && a.AccountingMonth <= yearTo))
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.GarageId,
                a.IncomeTypeId,
                a.AccountingMonth,
                a.AccountingYear,
                a.IsAnnualAccrual,
                a.IsCanceled,
                a.Amount,
                HasBasis = a.Basis != null,
                a.FeeCampaignId,
                a.IrregularPaymentId
            }).ToListAsync(token);
        // Only IDs/actions/dates are read; audit reason, user identity and free text are not needed.
        var ids = accruals.Select(a => a.Id.ToString()).ToArray();
        var lifecycle = await db.AuditEvents.AsNoTracking().Where(a => a.EntityType == "accrual" && ids.Contains(a.EntityId!) &&
                (a.Action == "finance.accrual_canceled" ||
                 a.Action == "finance.accrual_restored" ||
                 a.Action == "finance.regular_accrual_canceled_without_tariff" ||
                 a.Action == "finance.regular_accrual_safely_recalculated"))
            .OrderBy(a => a.Id).Select(a => new { a.Id, a.EntityId, a.Action, a.CreatedAtUtc }).ToListAsync(token);
        var lastActions = lifecycle.GroupBy(a => a.EntityId!).ToDictionary(g => g.Key,
            g => g.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).First().Action);
        var campaigns = await db.FeeCampaigns.AsNoTracking().Where(c => !c.IsArchived && c.ClosedAtUtc == null &&
                c.StartsOn <= yearTo && (c.EndsOn == null || c.EndsOn >= yearFrom))
            .OrderBy(c => c.Id).Select(c => new
            {
                c.Id,
                c.IncomeTypeId,
                c.IncomeType.Code,
                c.StartsOn,
                c.EndsOn,
                c.ContributionAmount,
                c.AppliesToAllGarages
            }).ToListAsync(token);

        var services = await db.ChargeServiceSettings.AsNoTracking().Where(s => !s.IsArchived && s.IsRegular)
            .OrderBy(s => s.Id).Select(s => new
            {
                s.Id,
                s.IncomeTypeId,
                Code = s.IncomeType == null ? null : s.IncomeType.Code,
                IncomeArchived = s.IncomeType != null && s.IncomeType.IsArchived,
                s.PeriodicityMonths,
                s.AccrualStartMonth,
                s.PaymentDueDay,
                s.PaymentDueMonth,
                s.OverdueGraceDays,
                s.AppliesToSelectedGarages,
                s.GarageIds,
                s.Version
            }).ToListAsync(token);
        var annual = services.Where(s => AnnualAccrualPolicy.IsAnnual(s.PeriodicityMonths, s.Code)).ToArray();
        var reasons = new Dictionary<string, int>();
        if (!ValidEpoch(policy.SingleOrDefault(s => s.Key == "finance.annual_accrual_guard_epoch")?.JsonValue,
                policy.Where(s => s.BooleanValue && s.Key.StartsWith(AnnualAccrualHoldPolicy.KeyPrefix, StringComparison.Ordinal))
                    .Select(s => s.Key[AnnualAccrualHoldPolicy.KeyPrefix.Length..]).ToHashSet(StringComparer.Ordinal)))
            Add(reasons, "annual_guard_state_invalid");
        if (year > today.Year) Add(reasons, "future_year_not_release_forecast");
        if (year < today.Year) Add(reasons, "historical_year_requires_explicit_catch_up");
        var unsupportedCampaigns = campaigns.Count(c =>
        {
            var candidates = services.Where(s => s.IncomeTypeId == c.IncomeTypeId).ToArray();
            // Multiple services produce unknown persisted classification. A held
            // year can block even an apparently monthly campaign in that state.
            return candidates.Length > 1 || (candidates.Length == 0
                ? AnnualAccrualPolicy.IsAnnualIncomeType(c.Code)
                : AnnualAccrualPolicy.IsAnnual(candidates[0].PeriodicityMonths, c.Code));
        });
        if (unsupportedCampaigns > 0) Add(reasons, "annual_fee_campaign_not_supported", unsupportedCampaigns);
        var monthlyInputs = new List<object>();
        var result = new List<AnnualAccrualServiceForecast>();
        var affectedGarages = new HashSet<Guid>();
        var repository = new EfChargeServiceSettingRepository(db);
        foreach (var raw in annual)
        {
            token.ThrowIfCancellationRequested();
            var excluded = new Dictionary<string, int>();
            var uncertain = new Dictionary<string, int>();
            var applicable = garages.Where(g => !raw.AppliesToSelectedGarages || raw.GarageIds.Contains(g.Id)).ToArray();
            var count = 0;
            var amount = 0m;
            var existingCount = 0;
            var existingAmount = 0m;
            var month = new DateOnly(year, raw.AccrualStartMonth is >= 1 and <= 12 ? raw.AccrualStartMonth.Value : 1, 1);
            var settings = await repository.GetActiveRegularAsync(month, token);
            var setting = settings.SingleOrDefault(s => s.Id == raw.Id);
            if (setting is null)
            {
                Add(uncertain, "annual_service_snapshot_unavailable", Math.Max(applicable.Length, 1));
                monthlyInputs.Add(new { raw.Id, month, Unavailable = true });
                result.Add(new(raw.Id, raw.IncomeTypeId, applicable.Length, 0, 0m, 0, 0m,
                    ToReasons(excluded), ToReasons(uncertain)));
                continue;
            }
            monthlyInputs.Add(new
            {
                raw.Id,
                month,
                setting.TariffId,
                setting.IsMetered,
                setting.HasTieredTariff,
                Tariff = TariffInput(setting.Tariff),
                Versions = setting.TariffVersions.OrderBy(v => v.EffectiveFrom)
                    .Select(v => new { v.EffectiveFrom, v.EffectiveTo, v.IsArchived, Tariff = TariffInput(v.Tariff) }).ToArray()
            });
            var duplicateIncome = raw.IncomeTypeId.HasValue && services.Count(s => s.IncomeTypeId == raw.IncomeTypeId) > 1;
            foreach (var garage in applicable)
            {
                if (!raw.IncomeTypeId.HasValue || raw.IncomeArchived || raw.PeriodicityMonths is null or < 12 || raw.AccrualStartMonth is null or < 1 or > 12)
                { Add(uncertain, "annual_service_configuration_invalid"); continue; }
                if (duplicateIncome) { Add(uncertain, "income_type_has_multiple_regular_services"); continue; }
                if (month > monthNow) { Add(excluded, "scheduled_after_business_month"); continue; }
                var start = GarageAccountingPeriod.ResolveStartMonth(garage.AccountingStartMonth,
                    garage.RegisteredOn ?? businessDate.ToBusinessDate(garage.CreatedAtUtc));
                if (start.Year > year || garage.AccountingStartMonth.HasValue && start > monthNow)
                { Add(excluded, "accounting_not_started"); continue; }
                var related = accruals.Where(a => a.GarageId == garage.Id && a.IncomeTypeId == raw.IncomeTypeId).ToArray();
                var existing = related.Where(a => !a.IsCanceled &&
                    (a.AccountingYear == year || a.AccountingYear == null && a.AccountingMonth.Year == year)).ToArray();
                if (existing.Length > 0)
                {
                    existingCount += existing.Length;
                    existingAmount += existing.Sum(a => a.Amount);
                    Add(excluded, "already_accrued");
                    if (existing.Length > 1) Add(uncertain, "duplicate_active_annual_accruals");
                    continue;
                }
                var canceled = related.Any(a => a.IsCanceled && !a.HasBasis && a.FeeCampaignId == null && a.IrregularPaymentId == null &&
                    (a.AccountingMonth == month || (a.AccountingYear ?? (a.IsAnnualAccrual == true ? a.AccountingMonth.Year : (int?)null)) == year) &&
                    lastActions.GetValueOrDefault(a.Id.ToString()) == "finance.accrual_canceled");
                if (canceled) { Add(excluded, "manually_canceled"); continue; }
                // The catalogue and per-garage catch-up differ when the first month is shifted.
                // Do not claim an exact release amount until that difference is resolved.
                if (start > month) { Add(uncertain, "shifted_first_annual_month"); continue; }
                if (!setting.AppliesToSelectedGarages && assignments.Any(a => a.GarageId == garage.Id && a.ChargeServiceSettingId == raw.Id &&
                        a.EffectiveFrom <= month.AddMonths(1).AddDays(-1) && (a.EffectiveTo == null || a.EffectiveTo >= month)))
                { Add(uncertain, "individual_tariff_not_supported"); continue; }
                var tariff = setting.Tariff ?? setting.TariffVersions.Where(v => !v.IsArchived && !v.Tariff.IsArchived)
                    .OrderBy(v => v.EffectiveFrom).Select(v => v.Tariff).FirstOrDefault();
                if (tariff is null || tariff.IsArchived) { Add(excluded, "no_effective_tariff"); continue; }
                if (setting.IsMetered || setting.HasTieredTariff || tariff.CalculationBase is not (TariffCalculationBases.Fixed or TariffCalculationBases.People))
                { Add(uncertain, "metered_or_tiered_annual_not_supported"); continue; }
                var versions = setting.TariffVersions.Where(v => !v.IsArchived && !v.Tariff.IsArchived).ToArray();
                var end = month.AddMonths(1).AddDays(-1);
                if (tariff.EffectiveFrom > month || versions.Length > 1 || versions.Any(v => v.EffectiveFrom > month || v.EffectiveTo.HasValue && v.EffectiveTo < end || v.TariffId != tariff.Id))
                { Add(uncertain, "partial_or_changing_tariff_not_supported"); continue; }
                var history = people.Where(p => p.GarageId == garage.Id && p.EffectiveFrom <= end)
                    .Select(p => new GaragePeopleCountPeriod { GarageId = p.GarageId, EffectiveFrom = p.EffectiveFrom, PeopleCount = p.PeopleCount }).ToArray();
                var calculated = RegularAccrualCalculator.Calculate(new Garage { Id = garage.Id, Number = string.Empty, PeopleCount = garage.PeopleCount, FloorCount = garage.FloorCount },
                    month, null, [new(month, end, tariff.CalculationBase, tariff.Rate, string.Empty, [])], history);
                if (!calculated.Succeeded) { Add(uncertain, "calculation_not_supported"); continue; }
                if (calculated.Amount <= 0m) { Add(excluded, "zero_amount"); continue; }
                count++;
                affectedGarages.Add(garage.Id);
                amount += calculated.Amount;
            }
            result.Add(new(raw.Id, raw.IncomeTypeId, applicable.Length, count, amount, existingCount, existingAmount,
                ToReasons(excluded), ToReasons(uncertain)));
        }
        var fingerprint = AccountingCleanupSchema.Hash(JsonSerializer.Serialize(new
        {
            Version = 1,
            Scope,
            year,
            today,
            businessDate.TimeZoneId,
            source,
            policy,
            garages,
            people,
            assignments,
            accruals,
            lifecycle,
            campaigns,
            services,
            monthlyInputs
        }, Json));
        var complete = reasons.Count == 0 && result.All(s => s.Uncertainties.Count == 0);
        var report = new AnnualAccrualForecast(1, year, today, businessDate.TimeZoneId, clock.GetUtcNow(), source, holdActive,
            Scope, garages.Count, result.Sum(s => s.NewAccrualCount), result.Sum(s => s.NewAccrualAmount), result,
            ToReasons(reasons), complete, fingerprint, string.Empty)
        { AffectedGarageCount = affectedGarages.Count };
        return report with { Fingerprint = AccountingCleanupSchema.Hash(JsonSerializer.Serialize(report, Json)) };
    }

    private static object? TariffInput(Tariff? t) => t is null ? null : new
    {
        t.Id,
        t.CalculationBase,
        t.Rate,
        t.EffectiveFrom,
        t.IsArchived,
        t.Version,
        t.ElectricityTiersJson,
        t.ElectricityFirstThreshold,
        t.ElectricitySecondThreshold,
        t.ElectricityFirstRate,
        t.ElectricitySecondRate,
        t.ElectricityThirdRate
    };
    private static bool ValidEpoch(string? json, HashSet<string> expected)
    {
        if (json is null) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.EnumerateObject())
                if (entry.Name.Length != 4 || entry.Name.Any(c => c is < '0' or > '9') ||
                    entry.Value.ValueKind != JsonValueKind.True || !actual.Add(entry.Name)) return false;
            return actual.SetEquals(expected);
        }
        catch (JsonException) { return false; }
    }
    private static void Add(Dictionary<string, int> reasons, string code, int count = 1) => reasons[code] = reasons.GetValueOrDefault(code) + count;
    private static IReadOnlyList<AnnualAccrualForecastReason> ToReasons(Dictionary<string, int> reasons) =>
        reasons.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new AnnualAccrualForecastReason(p.Key, p.Value)).ToArray();
    private static AccountingCleanupException Error(string code) => new(code, "Не удалось построить безопасный прогноз годовых начислений.");
    private sealed record GarageInput(Guid Id, int PeopleCount, int FloorCount, DateOnly? AccountingStartMonth, DateOnly? RegisteredOn, DateTimeOffset CreatedAtUtc);
}
