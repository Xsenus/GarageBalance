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

        var regularResult = await financeService.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(accountingMonth, "Автоматическое ежемесячное формирование"),
            actorUserId,
            cancellationToken);
        var regularCreatedCount = regularResult.Succeeded ? regularResult.Value!.CreatedCount : 0;
        var regularSkippedCount = regularResult.Succeeded ? regularResult.Value!.SkippedCount : 0;
        var failures = new List<string>();
        if (!regularResult.Succeeded && regularResult.ErrorCode is not ("regular_catalog_empty" or "regular_catalog_accruals_empty"))
        {
            failures.Add($"регулярные услуги: {regularResult.ErrorCode}");
        }

        var feeCampaignResult = await financeService.GenerateActiveFeeCampaignAccrualsAsync(
            new GenerateActiveFeeCampaignAccrualsRequest(accountingMonth, "Автоматическое начисление действующих сборов"),
            actorUserId,
            cancellationToken);
        var feeCampaignCreatedCount = feeCampaignResult.Succeeded ? feeCampaignResult.Value!.CreatedCount : 0;
        var feeCampaignSkippedCount = feeCampaignResult.Succeeded ? feeCampaignResult.Value!.SkippedCount : 0;
        if (!feeCampaignResult.Succeeded)
        {
            failures.Add($"действующие сборы: {feeCampaignResult.ErrorCode}");
        }
        else
        {
            failures.AddRange(feeCampaignResult.Value!.FailedCampaigns.Select(failure => $"сбор: {failure}"));
        }

        var createdCount = regularCreatedCount + feeCampaignCreatedCount;
        var skippedCount = regularSkippedCount + feeCampaignSkippedCount;
        if (failures.Count > 0)
        {
            logger.LogWarning(
                "Accrual automation did not complete for {AccountingMonth}: {Failures}.",
                accountingMonth,
                string.Join("; ", failures));
            return new RegularAccrualAutomationRunResult(
                false,
                createdCount,
                skippedCount,
                $"Начисления за {accountingMonth:MM.yyyy} созданы частично: {string.Join("; ", failures)}. Фоновая задача повторит попытку.");
        }

        logger.LogInformation(
            "Accrual automation completed for {AccountingMonth}: regular created {RegularCreatedCount}, fee campaign created {FeeCampaignCreatedCount}, skipped {SkippedCount}.",
            accountingMonth,
            regularCreatedCount,
            feeCampaignCreatedCount,
            skippedCount);
        return new RegularAccrualAutomationRunResult(
            true,
            createdCount,
            skippedCount,
            $"Начисления за {accountingMonth:MM.yyyy}: регулярные услуги — создано {regularCreatedCount}; действующие сборы — создано {feeCampaignCreatedCount}; пропущено {skippedCount}.");
    }
}
