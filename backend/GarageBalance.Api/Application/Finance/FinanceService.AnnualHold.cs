namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService
{
    private const string AnnualHoldErrorCode = "annual_accrual_generation_held";
    private Task<bool> IsAnnualYearHeldAsync(int? year, CancellationToken token) =>
        annualAccrualHoldPolicy?.IsHeldAsync(year, token) ?? Task.FromResult(false);

    private async Task<int?> ResolveAnnualYearForIncomeAsync(GarageBalance.Api.Domain.Dictionaries.IncomeType incomeType,
        DateOnly month, CancellationToken token)
    {
        var setting = SelectChargeServiceSettingForDueDates(
            await chargeServiceSettingRepository.GetActiveRegularForDueDatesAsync(incomeType.Id, null, month, token), month);
        return GarageBalance.Api.Domain.Finance.AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month, setting?.PeriodicityMonths);
    }

    private async Task<(int? Year, bool? Classification)> ResolveAnnualStatusForIncomeAsync(
        GarageBalance.Api.Domain.Dictionaries.IncomeType incomeType, DateOnly month, CancellationToken token)
    {
        var candidates = await chargeServiceSettingRepository.GetActiveRegularForDueDatesAsync(incomeType.Id, null, month, token);
        var selected = SelectChargeServiceSettingForDueDates(candidates, month);
        var year = GarageBalance.Api.Domain.Finance.AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month, selected?.PeriodicityMonths);
        bool? classification = candidates.Count > 1 ? null :
            GarageBalance.Api.Domain.Finance.AnnualAccrualPolicy.IsAnnual(candidates.FirstOrDefault()?.PeriodicityMonths, incomeType.Code);
        return (year, classification);
    }

    // A campaign/manual accrual has no selected service ID. Do not persist a guess
    // when more than one service can determine its annual classification.
    private async Task<bool?> ResolveAnnualClassificationForIncomeAsync(GarageBalance.Api.Domain.Dictionaries.IncomeType incomeType,
        DateOnly month, CancellationToken token)
    {
        var candidates = await chargeServiceSettingRepository.GetActiveRegularForDueDatesAsync(incomeType.Id, null, month, token);
        if (candidates.Count > 1) return null;
        // New unlinked accruals follow the current active service, never an archived one.
        // Historical rows retain null and the database applies the conservative fallback.
        return GarageBalance.Api.Domain.Finance.AnnualAccrualPolicy.IsAnnual(candidates.FirstOrDefault()?.PeriodicityMonths, incomeType.Code);
    }

    private async Task<IReadOnlyList<int>> GetHeldAnnualYearsAsync(DateOnly from, DateOnly through, CancellationToken token)
    {
        var years = new List<int>();
        for (var year = from.Year; year <= through.Year; year++)
            if (await IsAnnualYearHeldAsync(year, token)) years.Add(year);
        return years;
    }
}
