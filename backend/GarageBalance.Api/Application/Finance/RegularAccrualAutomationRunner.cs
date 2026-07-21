using Microsoft.Extensions.Options;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Finance;

public interface IRegularAccrualAutomationRunner
{
    Task<RegularAccrualAutomationRunResult> RunCurrentMonthAsync(CancellationToken cancellationToken);
    Task<RegularAccrualAutomationRunResult> RunForDateAsync(DateOnly businessDate, Guid? actorUserId, CancellationToken cancellationToken);
}

public sealed record RegularAccrualAutomationRunResult(
    bool Succeeded,
    int CreatedCount,
    int SkippedCount,
    string Message);

public sealed class RegularAccrualAutomationRunner(
    IFinanceService financeService,
    IBusinessDateProvider businessDateProvider,
    ILogger<RegularAccrualAutomationRunner> logger) : IRegularAccrualAutomationRunner
{
    public Task<RegularAccrualAutomationRunResult> RunCurrentMonthAsync(CancellationToken cancellationToken) =>
        RunForDateAsync(businessDateProvider.Today, actorUserId: null, cancellationToken);

    public async Task<RegularAccrualAutomationRunResult> RunForDateAsync(
        DateOnly businessDate,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var accountingMonth = new DateOnly(businessDate.Year, businessDate.Month, 1);

        var result = await financeService.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(accountingMonth, "Автоматическое ежемесячное формирование"),
            actorUserId,
            cancellationToken);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Regular accrual automation completed for {AccountingMonth}: created {CreatedCount}, skipped {SkippedCount}.",
                accountingMonth,
                result.Value!.CreatedCount,
                result.Value.SkippedCount);
            return new RegularAccrualAutomationRunResult(
                true,
                result.Value.CreatedCount,
                result.Value.SkippedCount,
                $"Начисления за {accountingMonth:MM.yyyy}: создано {result.Value.CreatedCount}, пропущено {result.Value.SkippedCount}.");
        }

        if (result.ErrorCode is "regular_catalog_empty" or "regular_catalog_accruals_empty")
        {
            logger.LogInformation(
                "Regular accrual automation has no rows to create for {AccountingMonth}: {ErrorCode}.",
                accountingMonth,
                result.ErrorCode);
            return new RegularAccrualAutomationRunResult(
                true,
                0,
                0,
                $"За {accountingMonth:MM.yyyy} нет настроенных регулярных начислений.");
        }

        logger.LogWarning(
            "Regular accrual automation did not complete for {AccountingMonth}: {ErrorCode}.",
            accountingMonth,
            result.ErrorCode);
        return new RegularAccrualAutomationRunResult(
            false,
            0,
            0,
            $"Рабочая дата сохранена, но автоматическое формирование начислений не завершилось ({result.ErrorCode}). Фоновая задача повторит попытку.");
    }
}
