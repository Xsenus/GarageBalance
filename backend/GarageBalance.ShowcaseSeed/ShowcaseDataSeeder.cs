using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.ShowcaseSeed;

public sealed class ShowcaseDataSeeder(GarageBalanceDbContext context)
{
    public const string Marker = "showcase_seed_v1";
    public static readonly DateOnly AccountingMonth = new(2026, 8, 1);
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 14, 4, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions PersistedJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] GarageNumbers =
    [
        "101-БЕЗ-ДОЛГА",
        "102-ЧАСТИЧНО",
        "103-ДОЛЖНИК",
        "104-АВАНС",
        "105-ПОРОГ-1",
        "106-ПОРОГ-2",
        "107-ПОРОГ-3",
        "108-СБОР",
        "109-ПРОСРОЧКА",
        "110-НОВЫЙ"
    ];

    public async Task<ShowcaseSeedResult> PrepareAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(1732042601);",
            cancellationToken);

        await ClearBusinessDataAsync(cancellationToken);
        var services = await LoadServicesAsync(cancellationToken);
        await EnsureRepresentativeTariffsAsync(services, cancellationToken);
        ConfigureRepresentativeTariffs(services);
        await RebuildTariffHistoryAsync(services, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var garages = CreateGarages();
        context.Garages.AddRange(garages);
        await context.SaveChangesAsync(cancellationToken);

        var readings = CreateReadings(garages);
        context.MeterReadings.AddRange(readings);
        var accruals = CreateRegularAccruals(garages, services, readings);
        context.Accruals.AddRange(accruals);
        await context.SaveChangesAsync(cancellationToken);

        var payments = CreatePaymentsAndAllocations(garages, accruals);
        context.AddRange(payments.Operations);
        context.AddRange(payments.Allocations);

        var overdueData = await CreateOverdueExampleAsync(garages, cancellationToken);
        context.AddRange(overdueData.Accruals);
        context.AddRange(overdueData.Operations);
        context.AddRange(overdueData.Allocations);

        var campaignData = await CreateFeeCampaignsAsync(garages, cancellationToken);
        context.AddRange(campaignData.Accruals);
        context.AddRange(campaignData.Operations);
        context.AddRange(campaignData.Allocations);

        var irregularData = await CreateIrregularExamplesAsync(garages, cancellationToken);
        context.AddRange(irregularData.Accruals);
        context.AddRange(irregularData.Operations);
        context.AddRange(irregularData.Allocations);

        await CreateSupplierAndExpenseExampleAsync(services, cancellationToken);
        await CreateCashAndBankExamplesAsync(cancellationToken);
        await CreateFundMovementExamplesAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            "ANALYZE owners; ANALYZE garages; ANALYZE accruals; ANALYZE financial_operations; ANALYZE fund_operations; ANALYZE meter_readings;",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return await AuditAsync(cancellationToken);
    }

    public async Task<ShowcaseSeedResult> AuditAsync(CancellationToken cancellationToken)
    {
        var garages = await context.Garages.CountAsync(item => item.Comment == Marker, cancellationToken);
        var accruals = await context.Accruals.CountAsync(item => item.Comment == Marker, cancellationToken);
        var payments = await context.FinancialOperations.CountAsync(item => item.Comment == Marker, cancellationToken);
        var readings = await context.MeterReadings.CountAsync(item => item.Comment == Marker, cancellationToken);
        var campaigns = await context.FeeCampaigns.CountAsync(item => item.Goal != null && item.Goal.Contains(Marker), cancellationToken);
        var suppliers = await context.Suppliers.CountAsync(item => item.Comment == Marker, cancellationToken);
        var fundOperations = await context.FundOperations.CountAsync(item => item.Reason.Contains(Marker), cancellationToken);
        var users = await context.Users.CountAsync(cancellationToken);
        var hasValidElectricityTiers = await HasValidElectricityTiersAsync(cancellationToken);
        var newGarageId = DeterministicGuid("garage-10");
        var overdueGarageId = DeterministicGuid("garage-9");

        var debtRows = await (
                from garage in context.Garages
                where garage.Comment == Marker
                let charged = context.Accruals
                    .Where(item => item.GarageId == garage.Id && !item.IsCanceled)
                    .Sum(item => (decimal?)item.Amount) ?? 0m
                let paid = context.FinancialOperations
                    .Where(item => item.GarageId == garage.Id
                        && item.OperationKind == FinancialOperationKinds.Income
                        && !item.IsCanceled)
                    .Sum(item => (decimal?)item.Amount) ?? 0m
                select new { garage.Number, Balance = paid - charged })
            .ToListAsync(cancellationToken);

        var hasNoDebt = debtRows.Any(item => item.Balance == 0m);
        var hasDebt = debtRows.Any(item => item.Balance < 0m);
        var hasAdvance = debtRows.Any(item => item.Balance > 0m);
        var newGarageHasNoCalculatedHistory = !await context.Accruals.AnyAsync(
                item => item.GarageId == newGarageId,
                cancellationToken)
            && !await context.FinancialOperations.AnyAsync(
                item => item.GarageId == newGarageId,
                cancellationToken);
        var campaignsHaveLockedParticipants = await context.FeeCampaigns
            .Where(item => item.Goal != null && item.Goal.Contains(Marker))
            .AllAsync(item =>
                item.ParticipantGarages.Count == GarageNumbers.Length - 1
                && item.ParticipantGarages.All(participant => participant.GarageId != newGarageId),
                cancellationToken);
        var annualAccrualsAreUnique = await HasUniqueAnnualAccrualsAsync(newGarageId, cancellationToken);
        var overdueScenarioIsCorrect = await HasExpectedOverdueScenarioAsync(overdueGarageId, cancellationToken);
        var isReady = garages == GarageNumbers.Length
            && accruals == 65
            && payments == 8
            && readings == (GarageNumbers.Length - 1) * 4
            && campaigns == 2
            && suppliers == 1
            && fundOperations == 2
            && hasNoDebt
            && hasDebt
            && hasAdvance
            && hasValidElectricityTiers
            && newGarageHasNoCalculatedHistory
            && campaignsHaveLockedParticipants
            && annualAccrualsAreUnique
            && overdueScenarioIsCorrect;

        return new ShowcaseSeedResult(
            isReady,
            garages,
            accruals,
            payments,
            readings,
            campaigns,
            suppliers,
            users,
            hasNoDebt,
            hasDebt,
            hasAdvance,
            newGarageHasNoCalculatedHistory,
            campaignsHaveLockedParticipants,
            annualAccrualsAreUnique,
            overdueScenarioIsCorrect);
    }

    private async Task ClearBusinessDataAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            TRUNCATE TABLE
                access_import_created_records,
                access_import_quarantine_items,
                access_import_row_fingerprints,
                access_import_run_log_entries,
                access_import_runs,
                garage_report_quick_list_garages,
                garage_report_quick_lists,
                accrual_payment_allocations,
                fund_operations,
                financial_operations,
                supplier_accruals,
                staff_salary_adjustments,
                accruals,
                meter_readings,
                meter_devices,
                cash_bank_transfers,
                cash_bank_balance_operations,
                opening_balance_adjustments,
                fee_campaign_garages,
                fee_campaigns,
                supplier_contacts,
                suppliers,
                supplier_groups,
                staff_members,
                staff_departments,
                garages,
                owners
            CASCADE;
            TRUNCATE TABLE audit_events;
            """;
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        await context.Funds.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(item => item.Balance, 0m)
                .SetProperty(item => item.UpdatedAtUtc, CreatedAtUtc)
                .SetProperty(item => item.Version, Guid.NewGuid()),
            cancellationToken);
        context.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyDictionary<string, ChargeServiceSetting>> LoadServicesAsync(
        CancellationToken cancellationToken)
    {
        var settings = await context.ChargeServiceSettings
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .Where(item => !item.IsArchived && item.IncomeType != null && item.IncomeType.Code != null)
            .ToListAsync(cancellationToken);
        var result = settings.ToDictionary(item => item.IncomeType!.Code!, StringComparer.Ordinal);
        string[] required = ["water", "trash", "outdoor_lighting", "target", "membership", "electricity"];
        var missing = required.Where(code => !result.ContainsKey(code)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Required service catalog is incomplete: {string.Join(", ", missing)}.");
        }

        return result;
    }

    private static void ConfigureRepresentativeTariffs(IReadOnlyDictionary<string, ChargeServiceSetting> services)
    {
        foreach (var setting in services.Values)
        {
            var isAnnual = AnnualAccrualPolicy.IsAnnualIncomeType(setting.IncomeType!.Code);
            setting.IsRegular = true;
            setting.IsArchived = false;
            setting.PeriodicityMonths = isAnnual ? 12 : 1;
            setting.AccrualStartMonth = isAnnual ? AccountingMonth.Month : 1;
            setting.PaymentDueDay = 20;
            setting.PaymentDueMonth = isAnnual ? AccountingMonth.AddMonths(1).Month : null;
            setting.OverdueGraceDays = 30;
            setting.UpdatedAtUtc = CreatedAtUtc;
            setting.Version = DeterministicGuid($"setting-version-{setting.IncomeType.Code}");
        }

        Configure(services["water"], TariffCalculationBases.MeterWater, 101m, true, false, MeterKinds.Water, "м³");
        Configure(services["trash"], TariffCalculationBases.People, 130m, false, false, null, "чел.");
        Configure(services["outdoor_lighting"], TariffCalculationBases.Fixed, 300m, false, false, null, "руб.");
        Configure(services["target"], TariffCalculationBases.Fixed, 1200m, false, false, null, "руб.");
        Configure(services["membership"], TariffCalculationBases.Fixed, 500m, false, false, null, "руб.");
        Configure(services["electricity"], TariffCalculationBases.MeterElectricity, 7.5m, true, true, MeterKinds.Electricity, "кВт·ч");

        var electricity = services["electricity"].Tariff!;
        electricity.ElectricityFirstThreshold = 1100m;
        electricity.ElectricitySecondThreshold = 1700m;
        electricity.ElectricityFirstRate = 7.5m;
        electricity.ElectricitySecondRate = 10m;
        electricity.ElectricityThirdRate = 15m;
        electricity.ElectricityTiersJson = CreateRepresentativeElectricityTiersJson();
    }

    internal static string CreateRepresentativeElectricityTiersJson() => JsonSerializer.Serialize(
        new ShowcaseElectricityTier[]
        {
            new ShowcaseElectricityTier(DeterministicGuid("electricity-tier-1"), "0–1100 кВт·ч", 1100m, 7.5m, false),
            new ShowcaseElectricityTier(DeterministicGuid("electricity-tier-2"), "1101–1700 кВт·ч", 1700m, 10m, false),
            new ShowcaseElectricityTier(DeterministicGuid("electricity-tier-3"), "1701+ кВт·ч", null, 15m, false)
        });

    private async Task<bool> HasValidElectricityTiersAsync(CancellationToken cancellationToken)
    {
        var tiersJson = await context.ChargeServiceSettings
            .AsNoTracking()
            .Where(item => item.IncomeType != null && item.IncomeType.Code == "electricity")
            .Select(item => item.Tariff!.ElectricityTiersJson)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(tiersJson))
        {
            return false;
        }

        try
        {
            var tiers = JsonSerializer.Deserialize<ShowcaseElectricityTier[]>(tiersJson, PersistedJsonOptions);
            return tiers is
                [
                { UpperBound: 1100m, Rate: 7.5m },
                { UpperBound: 1700m, Rate: 10m },
                { UpperBound: null, Rate: 15m }
                ]
            && tiers.All(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Name));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<bool> HasUniqueAnnualAccrualsAsync(Guid newGarageId, CancellationToken cancellationToken)
    {
        string[] annualCodes = ["membership", "target", "outdoor_lighting"];
        var annualRows = await context.Accruals
            .Where(item =>
                item.Comment == Marker
                && !item.IsCanceled
                && item.IncomeType.Code != null
                && annualCodes.Contains(item.IncomeType.Code))
            .GroupBy(item => new { item.GarageId, item.IncomeTypeId, item.AccountingYear })
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return annualRows.Count == (GarageNumbers.Length - 1) * annualCodes.Length
            && annualRows.All(item => item.Key.GarageId != newGarageId && item.Key.AccountingYear == 2026 && item.Count == 1);
    }

    private async Task<bool> HasExpectedOverdueScenarioAsync(Guid overdueGarageId, CancellationToken cancellationToken)
    {
        var row = await context.Accruals
            .Where(item => item.GarageId == overdueGarageId && item.Basis == "Частично оплаченная просрочка" && !item.IsCanceled)
            .Select(item => new
            {
                item.Amount,
                item.OverdueFromDate,
                Paid = context.AccrualPaymentAllocations
                    .Where(allocation => allocation.AccrualId == item.Id && !allocation.FinancialOperation.IsCanceled)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is { Amount: 1000m, OverdueFromDate: var overdueFromDate, Paid: 400m }
            && overdueFromDate == new DateOnly(2026, 8, 21);
    }

    private async Task EnsureRepresentativeTariffsAsync(
        IReadOnlyDictionary<string, ChargeServiceSetting> services,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["water"] = "ДЕМО Вода",
            ["trash"] = "ДЕМО Мусор",
            ["outdoor_lighting"] = "ДЕМО Наружное освещение",
            ["target"] = "ДЕМО Целевой взнос",
            ["membership"] = "ДЕМО Членский взнос",
            ["electricity"] = "ДЕМО Электроэнергия"
        };

        foreach (var (code, name) in names)
        {
            var tariffId = DeterministicGuid($"current-tariff-{code}");
            var tariff = await context.Tariffs
                .SingleOrDefaultAsync(item => item.Id == tariffId, cancellationToken);
            if (tariff is null)
            {
                tariff = new Tariff
                {
                    Id = tariffId,
                    Name = name,
                    CalculationBase = TariffCalculationBases.Fixed,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    CreatedAtUtc = CreatedAtUtc
                };
                context.Tariffs.Add(tariff);
            }

            tariff.Name = name;
            tariff.EffectiveFrom = new DateOnly(2026, 1, 1);
            tariff.IsArchived = false;
            tariff.UpdatedAtUtc = CreatedAtUtc;
            services[code].Tariff = tariff;
            services[code].TariffId = tariff.Id;
        }
    }

    private async Task RebuildTariffHistoryAsync(
        IReadOnlyDictionary<string, ChargeServiceSetting> services,
        CancellationToken cancellationToken)
    {
        await context.ChargeServiceTariffVersions.ExecuteDeleteAsync(cancellationToken);
        foreach (var (code, setting) in services)
        {
            if (code == "membership")
            {
                var previousTariffId = DeterministicGuid("membership-previous-tariff");
                var previousTariff = await context.Tariffs
                    .SingleOrDefaultAsync(item => item.Id == previousTariffId, cancellationToken);
                if (previousTariff is null)
                {
                    previousTariff = new Tariff { Id = previousTariffId, Name = string.Empty, CalculationBase = string.Empty };
                    context.Tariffs.Add(previousTariff);
                }

                previousTariff.Name = "Членский взнос до 01.07.2026";
                previousTariff.CalculationBase = TariffCalculationBases.Fixed;
                previousTariff.Rate = 450m;
                previousTariff.EffectiveFrom = new DateOnly(2026, 1, 1);
                previousTariff.Comment = "Историческая ставка для демонстрации тарифной сетки.";
                previousTariff.IsArchived = false;
                previousTariff.CreatedAtUtc = CreatedAtUtc;
                previousTariff.UpdatedAtUtc = CreatedAtUtc;
                previousTariff.Version = DeterministicGuid("membership-previous-tariff-version");
                context.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
                {
                    ChargeServiceSettingId = setting.Id,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    EffectiveTo = new DateOnly(2026, 6, 30),
                    Tariff = previousTariff,
                    CreatedAtUtc = CreatedAtUtc
                });
                context.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
                {
                    ChargeServiceSettingId = setting.Id,
                    EffectiveFrom = new DateOnly(2026, 7, 1),
                    TariffId = setting.TariffId!.Value,
                    CreatedAtUtc = CreatedAtUtc
                });
                continue;
            }

            context.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                TariffId = setting.TariffId!.Value,
                CreatedAtUtc = CreatedAtUtc
            });
        }
    }

    private static void Configure(
        ChargeServiceSetting setting,
        string calculationBase,
        decimal rate,
        bool isMetered,
        bool tiered,
        string? meterKind,
        string unit)
    {
        setting.IsMetered = isMetered;
        setting.HasTieredTariff = tiered;
        setting.MeterKind = meterKind;
        setting.UnitName = unit;
        setting.Tariff!.CalculationBase = calculationBase;
        setting.Tariff.Rate = rate;
        setting.Tariff.EffectiveFrom = new DateOnly(2026, 1, 1);
        setting.Tariff.Comment = "Демонстрационный тариф. Изменяется администратором в тарифной сетке.";
        setting.Tariff.UpdatedAtUtc = CreatedAtUtc;
        setting.Tariff.Version = DeterministicGuid($"tariff-version-{setting.IncomeType!.Code}");
    }

    private static Garage[] CreateGarages() => GarageNumbers
        .Select((number, index) => new Garage
        {
            Id = DeterministicGuid($"garage-{index + 1}"),
            Number = number,
            PeopleCount = index % 3 + 1,
            FloorCount = index % 2 + 1,
            StartingBalance = 0m,
            InitialWaterMeterValue = 100m + index * 10,
            InitialElectricityMeterValue = index switch
            {
                4 => 1000m,
                5 => 1400m,
                6 => 1900m,
                _ => 900m + index * 40m
            },
            Comment = Marker,
            Owner = new Owner
            {
                Id = DeterministicGuid($"owner-{index + 1}"),
                LastName = "Демонстрационный",
                FirstName = $"Сценарий {index + 1}",
                MiddleName = number[(number.IndexOf('-') + 1)..],
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc
            },
            CreatedAtUtc = index == GarageNumbers.Length - 1
                ? new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero)
                : CreatedAtUtc,
            UpdatedAtUtc = index == GarageNumbers.Length - 1
                ? new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero)
                : CreatedAtUtc,
            Version = DeterministicGuid($"garage-version-{index + 1}")
        })
        .ToArray();

    private static MeterReading[] CreateReadings(IReadOnlyList<Garage> garages)
    {
        var result = new List<MeterReading>();
        for (var index = 0; index < garages.Count; index++)
        {
            var garage = garages[index];
            if (garage.CreatedAtUtc > new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero))
            {
                continue;
            }

            var waterStart = garage.InitialWaterMeterValue!.Value;
            var electricityStart = garage.InitialElectricityMeterValue!.Value;
            result.Add(Reading(garage, MeterKinds.Water, new DateOnly(2026, 7, 1), waterStart, waterStart + 5 + index, index));
            result.Add(Reading(garage, MeterKinds.Water, AccountingMonth, waterStart + 5 + index, waterStart + 12 + index * 2, index));
            result.Add(Reading(garage, MeterKinds.Electricity, new DateOnly(2026, 7, 1), electricityStart, electricityStart + 40 + index * 2, index));
            var augustStart = electricityStart + 40 + index * 2;
            result.Add(Reading(garage, MeterKinds.Electricity, AccountingMonth, augustStart, augustStart + 50 + index * 3, index));
        }

        return result.ToArray();
    }

    private static MeterReading Reading(
        Garage garage,
        string kind,
        DateOnly month,
        decimal previous,
        decimal current,
        int index) => new()
        {
            Id = DeterministicGuid($"reading-{garage.Number}-{kind}-{month:yyyyMM}"),
            GarageId = garage.Id,
            MeterKind = kind,
            AccountingMonth = month,
            ReadingDate = month.AddDays(24),
            PreviousValue = previous,
            CurrentValue = current,
            Consumption = current - previous,
            Comment = Marker,
            Version = DeterministicGuid($"reading-version-{garage.Number}-{kind}-{month:yyyyMM}-{index}"),
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };

    private static Accrual[] CreateRegularAccruals(
        IReadOnlyList<Garage> garages,
        IReadOnlyDictionary<string, ChargeServiceSetting> services,
        IReadOnlyList<MeterReading> readings)
    {
        var monthEnd = AccountingMonth.AddMonths(1).AddDays(-1);
        var result = new List<Accrual>();
        foreach (var garage in garages)
        {
            var registeredOn = DateOnly.FromDateTime(garage.CreatedAtUtc.UtcDateTime);
            if (new DateOnly(registeredOn.Year, registeredOn.Month, 1) > AccountingMonth)
            {
                continue;
            }

            foreach (var (code, setting) in services.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var tariff = setting.Tariff!;
                var meter = setting.IsMetered
                    ? readings.Single(item => item.GarageId == garage.Id
                        && item.AccountingMonth == AccountingMonth
                        && item.MeterKind == setting.MeterKind)
                    : null;
                var tiers = setting.HasTieredTariff
                    ? new RegularAccrualTariffTier[]
                    {
                        new(tariff.ElectricityFirstThreshold, tariff.ElectricityFirstRate!.Value),
                        new(tariff.ElectricitySecondThreshold, tariff.ElectricitySecondRate!.Value),
                        new(null, tariff.ElectricityThirdRate!.Value)
                    }
                    : [];
                var calculation = RegularAccrualCalculator.Calculate(
                    garage,
                    AccountingMonth,
                    meter,
                    [new RegularAccrualSegmentDefinition(AccountingMonth, monthEnd, tariff.CalculationBase, tariff.Rate, setting.UnitName ?? string.Empty, tiers)]);
                if (!calculation.Succeeded)
                {
                    throw new InvalidOperationException($"Calculation failed for {garage.Number}/{code}: {calculation.ErrorMessage}");
                }

                var dueDates = AccrualDueDates.ForGarage(AccountingMonth, code, setting, registeredOn);
                result.Add(new Accrual
                {
                    Id = DeterministicGuid($"accrual-{garage.Number}-{code}"),
                    GarageId = garage.Id,
                    IncomeTypeId = setting.IncomeTypeId!.Value,
                    TariffId = tariff.Id,
                    AccountingMonth = AccountingMonth,
                    AccountingYear = AnnualAccrualPolicy.ResolveAccountingYear(code, AccountingMonth, setting.PeriodicityMonths),
                    DueDate = dueDates.DueDate,
                    OverdueFromDate = dueDates.OverdueFromDate,
                    Amount = calculation.Amount,
                    RequiresMeterReading = setting.IsMetered,
                    CalculationMeterKind = setting.MeterKind,
                    CalculationDetailsJson = RegularAccrualCalculator.Serialize(calculation.Details!),
                    Source = AccrualSources.Regular,
                    Comment = Marker,
                    CreatedAtUtc = CreatedAtUtc,
                    UpdatedAtUtc = CreatedAtUtc
                });
            }
        }

        return result.ToArray();
    }

    private static PaymentData CreatePaymentsAndAllocations(
        IReadOnlyList<Garage> garages,
        IReadOnlyList<Accrual> accruals)
    {
        var operations = new List<FinancialOperation>();
        var allocations = new List<AccrualPaymentAllocation>();
        AddPayment(garages[0], 1m);
        AddPayment(garages[1], 0.5m);
        AddPayment(garages[3], 1m, 2500m);
        return new PaymentData(operations, allocations);

        void AddPayment(Garage garage, decimal share, decimal advance = 0m)
        {
            var garageAccruals = accruals.Where(item => item.GarageId == garage.Id).ToArray();
            var allocated = garageAccruals.Sum(item => MoneyMath.RoundMoney(item.Amount * share));
            var operation = new FinancialOperation
            {
                Id = DeterministicGuid($"payment-{garage.Number}"),
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 8, 14),
                AccountingMonth = AccountingMonth,
                Amount = allocated + advance,
                GarageId = garage.Id,
                DocumentNumber = $"ДЕМО-{garage.Number}",
                Comment = Marker,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc
            };
            operations.Add(operation);
            allocations.AddRange(garageAccruals.Select(item => new AccrualPaymentAllocation
            {
                Id = DeterministicGuid($"allocation-{garage.Number}-{item.Id}"),
                FinancialOperation = operation,
                AccrualId = item.Id,
                Amount = MoneyMath.RoundMoney(item.Amount * share),
                CreatedAtUtc = CreatedAtUtc
            }));
        }
    }

    private async Task<CampaignData> CreateOverdueExampleAsync(
        IReadOnlyList<Garage> garages,
        CancellationToken cancellationToken)
    {
        var water = await context.IncomeTypes.SingleAsync(item => item.Code == "water", cancellationToken);
        var garage = garages[8];
        var accrual = new Accrual
        {
            Id = DeterministicGuid("overdue-accrual"),
            GarageId = garage.Id,
            IncomeTypeId = water.Id,
            Basis = "Частично оплаченная просрочка",
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 7, 20),
            OverdueFromDate = new DateOnly(2026, 8, 21),
            Amount = 1000m,
            Source = AccrualSources.Manual,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var operation = new FinancialOperation
        {
            Id = DeterministicGuid("overdue-payment"),
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 20),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 400m,
            GarageId = garage.Id,
            IncomeTypeId = water.Id,
            DocumentNumber = "ДЕМО-ПРОСРОЧКА",
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var allocation = new AccrualPaymentAllocation
        {
            Id = DeterministicGuid("overdue-allocation"),
            FinancialOperation = operation,
            Accrual = accrual,
            Amount = 400m,
            CreatedAtUtc = CreatedAtUtc
        };

        return new CampaignData([accrual], [operation], [allocation]);
    }

    private async Task<CampaignData> CreateFeeCampaignsAsync(
        IReadOnlyList<Garage> garages,
        CancellationToken cancellationToken)
    {
        var income = await context.IncomeTypes.SingleAsync(item => item.Code == "other_income", cancellationToken);
        var active = new FeeCampaign
        {
            Id = DeterministicGuid("campaign-active"),
            Name = "Ремонт ворот — собирается",
            IncomeTypeId = income.Id,
            Goal = $"Замена автоматики; {Marker}",
            ContributionAmount = 1000m,
            TargetAmount = 8000m,
            StartsOn = AccountingMonth,
            EndsOn = new DateOnly(2026, 9, 30),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var closed = new FeeCampaign
        {
            Id = DeterministicGuid("campaign-closed"),
            Name = "Покраска ворот — завершён",
            IncomeTypeId = income.Id,
            Goal = $"Показ завершённого сбора; {Marker}",
            ContributionAmount = 500m,
            TargetAmount = 4000m,
            StartsOn = new DateOnly(2026, 6, 1),
            EndsOn = new DateOnly(2026, 7, 31),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30,
            ClosedAtUtc = CreatedAtUtc.AddDays(-1),
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var participantGarages = garages
            .Where(garage => garage.CreatedAtUtc <= active.CreatedAtUtc && !garage.IsArchived)
            .ToArray();
        active.ParticipantGarages = participantGarages
            .Select(garage => new FeeCampaignGarage { FeeCampaign = active, GarageId = garage.Id })
            .ToList();
        closed.ParticipantGarages = participantGarages
            .Select(garage => new FeeCampaignGarage { FeeCampaign = closed, GarageId = garage.Id })
            .ToList();
        context.FeeCampaigns.AddRange(active, closed);

        var accruals = participantGarages.Select((garage, index) => new Accrual
        {
            Id = DeterministicGuid($"campaign-accrual-{garage.Number}"),
            GarageId = garage.Id,
            IncomeTypeId = income.Id,
            FeeCampaign = active,
            AccountingMonth = AccountingMonth,
            DueDate = new DateOnly(2026, 9, 20),
            OverdueFromDate = new DateOnly(2026, 10, 20),
            Amount = 1000m,
            Source = AccrualSources.FeeCampaign,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        }).ToArray();
        var operation = new FinancialOperation
        {
            Id = DeterministicGuid("campaign-payment"),
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 8, 14),
            AccountingMonth = AccountingMonth,
            Amount = 1500m,
            GarageId = garages[7].Id,
            IncomeTypeId = income.Id,
            FeeCampaign = active,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var allocation = new AccrualPaymentAllocation
        {
            Id = DeterministicGuid("campaign-allocation"),
            FinancialOperation = operation,
            AccrualId = accruals[7].Id,
            Amount = 1000m,
            CreatedAtUtc = CreatedAtUtc
        };
        var noDebtOperation = new FinancialOperation
        {
            Id = DeterministicGuid("campaign-payment-no-debt"),
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 8, 14),
            AccountingMonth = AccountingMonth,
            Amount = 1000m,
            GarageId = garages[0].Id,
            IncomeTypeId = income.Id,
            FeeCampaign = active,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var noDebtAllocation = new AccrualPaymentAllocation
        {
            Id = DeterministicGuid("campaign-allocation-no-debt"),
            FinancialOperation = noDebtOperation,
            AccrualId = accruals[0].Id,
            Amount = 1000m,
            CreatedAtUtc = CreatedAtUtc
        };
        return new CampaignData(accruals, [operation, noDebtOperation], [allocation, noDebtAllocation]);
    }

    private async Task<CampaignData> CreateIrregularExamplesAsync(
        IReadOnlyList<Garage> garages,
        CancellationToken cancellationToken)
    {
        var irregular = await context.IrregularPayments
            .OrderBy(item => item.Name)
            .FirstAsync(cancellationToken);
        irregular.IsActive = true;
        irregular.IsArchived = false;
        irregular.Amount = 5000m;
        irregular.UpdatedAtUtc = CreatedAtUtc;
        var income = await context.IncomeTypes.SingleAsync(item => item.Code == "other_payments", cancellationToken);
        var accrual = new Accrual
        {
            Id = DeterministicGuid("irregular-accrual"),
            GarageId = garages[2].Id,
            IncomeTypeId = income.Id,
            IrregularPaymentId = irregular.Id,
            Basis = irregular.Name,
            AccountingMonth = AccountingMonth,
            DueDate = new DateOnly(2026, 9, 20),
            OverdueFromDate = new DateOnly(2026, 10, 20),
            Amount = irregular.Amount,
            Source = AccrualSources.Manual,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var operation = new FinancialOperation
        {
            Id = DeterministicGuid("irregular-payment"),
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 8, 14),
            AccountingMonth = AccountingMonth,
            Amount = 2000m,
            GarageId = garages[2].Id,
            IncomeTypeId = income.Id,
            IrregularPaymentId = irregular.Id,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        var allocation = new AccrualPaymentAllocation
        {
            Id = DeterministicGuid("irregular-allocation"),
            FinancialOperation = operation,
            Accrual = accrual,
            Amount = 2000m,
            CreatedAtUtc = CreatedAtUtc
        };
        return new CampaignData([accrual], [operation], [allocation]);
    }

    private async Task CreateSupplierAndExpenseExampleAsync(
        IReadOnlyDictionary<string, ChargeServiceSetting> services,
        CancellationToken cancellationToken)
    {
        var expense = await context.ExpenseTypes.SingleAsync(item => item.Code == "electricity", cancellationToken);
        var fund = await context.Funds.OrderBy(item => item.SortOrder).FirstAsync(cancellationToken);
        var group = new SupplierGroup { Id = DeterministicGuid("supplier-group"), Name = "Коммунальные поставщики" };
        var supplier = new Supplier
        {
            Id = DeterministicGuid("supplier"),
            Name = "ДЕМО Энергосбыт",
            Group = group,
            ChargeServiceSettingId = services["electricity"].Id,
            ExpenseTypeId = expense.Id,
            ExpenseFundId = fund.Id,
            StartingBalance = -5000m,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc,
            Version = DeterministicGuid("supplier-version")
        };
        context.AddRange(group, supplier);
        context.SupplierAccruals.Add(new SupplierAccrual
        {
            Id = DeterministicGuid("supplier-accrual"),
            Supplier = supplier,
            ExpenseTypeId = expense.Id,
            ExpenseFundId = fund.Id,
            AccountingMonth = AccountingMonth,
            Amount = 12000m,
            Source = AccrualSources.Manual,
            DocumentNumber = "ДЕМО-СЧЕТ-001",
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        });
        context.FinancialOperations.Add(new FinancialOperation
        {
            Id = DeterministicGuid("supplier-payment"),
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 8, 14),
            AccountingMonth = AccountingMonth,
            Amount = 7000m,
            Supplier = supplier,
            ExpenseTypeId = expense.Id,
            ExpenseFundId = fund.Id,
            ExpensePaymentType = ExpensePaymentTypes.WithReceipt,
            ExpensePaymentSource = ExpensePaymentSources.Bank,
            DocumentNumber = "ДЕМО-ПП-001",
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        });
    }

    private Task CreateCashAndBankExamplesAsync(CancellationToken cancellationToken)
    {
        context.CashBankBalanceOperations.AddRange(
            new CashBankBalanceOperation
            {
                Id = DeterministicGuid("cash-opening"),
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = AccountingMonth,
                Amount = 10000m,
                Reason = "Демонстрационный стартовый остаток",
                CreatedAtUtc = CreatedAtUtc
            },
            new CashBankBalanceOperation
            {
                Id = DeterministicGuid("bank-opening"),
                Account = CashBankAccounts.Bank,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = AccountingMonth,
                Amount = 25000m,
                Reason = "Демонстрационный стартовый остаток",
                CreatedAtUtc = CreatedAtUtc
            });
        context.CashBankTransfers.Add(new CashBankTransfer
        {
            Id = DeterministicGuid("cash-bank-transfer"),
            TransferDate = new DateOnly(2026, 8, 14),
            Amount = 5000m,
            Comment = Marker,
            CreatedAtUtc = CreatedAtUtc
        });
        return Task.CompletedTask;
    }

    private async Task CreateFundMovementExamplesAsync(CancellationToken cancellationToken)
    {
        var fund = await context.Funds
            .OrderBy(item => item.SortOrder)
            .FirstAsync(cancellationToken);
        var supplierPayment = context.FinancialOperations.Local
            .Single(item => item.Id == DeterministicGuid("supplier-payment"));

        context.FundOperations.AddRange(
            new FundOperation
            {
                Id = DeterministicGuid("fund-deposit"),
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 27000m,
                BalanceBefore = 0m,
                BalanceAfter = 27000m,
                Reason = $"Демонстрационное распределение поступлений ({Marker})",
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc
            },
            new FundOperation
            {
                Id = DeterministicGuid("fund-withdrawal"),
                Fund = fund,
                SourceFinancialOperation = supplierPayment,
                OperationKind = FundOperationKinds.Withdraw,
                Amount = 7000m,
                BalanceBefore = 27000m,
                BalanceAfter = 20000m,
                Reason = $"Оплата демонстрационному поставщику ({Marker})",
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc
            });
        fund.Balance = 20000m;
        fund.UpdatedAtUtc = CreatedAtUtc;
        fund.Version = DeterministicGuid("fund-version");
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"garagebalance-showcase:{value}"));
        return new Guid(hash);
    }

    private sealed record PaymentData(
        IReadOnlyList<FinancialOperation> Operations,
        IReadOnlyList<AccrualPaymentAllocation> Allocations);

    private sealed record CampaignData(
        IReadOnlyList<Accrual> Accruals,
        IReadOnlyList<FinancialOperation> Operations,
        IReadOnlyList<AccrualPaymentAllocation> Allocations);

    private sealed record ShowcaseElectricityTier(
        Guid Id,
        string Name,
        decimal? UpperBound,
        decimal Rate,
        bool IsCustom);
}

public sealed record ShowcaseSeedResult(
    bool IsReady,
    int GarageCount,
    int AccrualCount,
    int FinancialOperationCount,
    int MeterReadingCount,
    int FeeCampaignCount,
    int SupplierCount,
    int PreservedUserCount,
    bool HasNoDebt,
    bool HasDebt,
    bool HasAdvance,
    bool NewGarageHasNoCalculatedHistory,
    bool CampaignsHaveLockedParticipants,
    bool AnnualAccrualsAreUnique,
    bool OverdueScenarioIsCorrect);
