using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Finance;

public sealed class RegularAccrualAutomationRunner(
    IFinanceService financeService,
    TimeProvider timeProvider,
    IOptions<RegularAccrualAutomationOptions> options,
    ILogger<RegularAccrualAutomationRunner> logger)
{
    private readonly RegularAccrualAutomationOptions _options = options.Value;

    public async Task RunCurrentMonthAsync(CancellationToken cancellationToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
        var businessNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
        var accountingMonth = new DateOnly(businessNow.Year, businessNow.Month, 1);

        var result = await financeService.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(accountingMonth, "Автоматическое ежемесячное формирование"),
            actorUserId: null,
            cancellationToken);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Regular accrual automation completed for {AccountingMonth}: created {CreatedCount}, skipped {SkippedCount}.",
                accountingMonth,
                result.Value!.CreatedCount,
                result.Value.SkippedCount);
            return;
        }

        if (result.ErrorCode is "regular_catalog_empty" or "regular_catalog_accruals_empty")
        {
            logger.LogInformation(
                "Regular accrual automation has no rows to create for {AccountingMonth}: {ErrorCode}.",
                accountingMonth,
                result.ErrorCode);
            return;
        }

        logger.LogWarning(
            "Regular accrual automation did not complete for {AccountingMonth}: {ErrorCode}.",
            accountingMonth,
            result.ErrorCode);
    }
}
