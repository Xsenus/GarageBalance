using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Settings;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Finance;

/// <summary>A garage whose unpaid accruals were rewritten by the multi-month worksheet calculation.</summary>
public sealed record AccrualPeriodRepairCandidate(Guid GarageId, DateOnly FirstMonth);

public sealed record AccrualPeriodRepairResult(bool AlreadyCompleted, int GarageCount, int RepairedCount, int FailedCount);

public interface IAccrualPeriodRepairQuery
{
    /// <summary>Garages with worksheet recalculations recorded in the audit and their earliest affected month.</summary>
    Task<IReadOnlyList<AccrualPeriodRepairCandidate>> GetCandidatesAsync(IReadOnlyCollection<string> actions, CancellationToken cancellationToken);
}

/// <summary>
/// Until 18.10.2026 the garage worksheet recalculated every month of a period with the tariff
/// versions of its last month, so unpaid charges of earlier months could be rewritten or
/// canceled. This one-time repair recalculates the affected garages with the corrected rules.
/// Paid and partially paid charges stay protected by the regular calculation.
/// </summary>
public sealed class AccrualPeriodRepairService(
    IServiceScopeFactory scopeFactory,
    ILogger<AccrualPeriodRepairService> logger)
{
    public const string CompletedSettingKey = "maintenance.accrual_period_repair_20261018";
    public static readonly string[] AffectedActions =
    [
        "finance.regular_accrual_recalculated_for_garage_worksheet",
        "finance.regular_accrual_excluded_from_worksheet_recalculation"
    ];

    private const int MaxMonthsPerCalculation = 600;

    public async Task<AccrualPeriodRepairResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AccrualPeriodRepairCandidate> candidates;
        DateOnly through;
        using (var scope = scopeFactory.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IApplicationSettingRepository>();
            if (await settings.FindAsync(CompletedSettingKey, cancellationToken) is not null)
            {
                return new AccrualPeriodRepairResult(true, 0, 0, 0);
            }

            candidates = await scope.ServiceProvider.GetRequiredService<IAccrualPeriodRepairQuery>()
                .GetCandidatesAsync(AffectedActions, cancellationToken);
            through = MonthPeriod.Normalize(scope.ServiceProvider.GetRequiredService<IBusinessDateProvider>().Today);
        }

        var repaired = 0;
        var failed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A separate scope per garage keeps one failed calculation from leaving tracked state for the next.
            using var scope = scopeFactory.CreateScope();
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceService>();
            try
            {
                var succeeded = true;
                for (var from = MonthPeriod.Normalize(candidate.FirstMonth); from <= through; from = from.AddMonths(MaxMonthsPerCalculation))
                {
                    var to = from.AddMonths(MaxMonthsPerCalculation - 1);
                    var result = await finance.CalculateGarageIncomeWorksheetAsync(
                        candidate.GarageId, new GarageIncomeWorksheetRequest(from, to < through ? to : through), null, cancellationToken);
                    if (!result.Succeeded)
                    {
                        succeeded = false;
                        logger.LogWarning(
                            "Accrual period repair skipped garage {GarageId}: {ErrorCode}.", candidate.GarageId, result.ErrorCode);
                        break;
                    }
                }

                if (succeeded) repaired++;
                else failed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "Accrual period repair failed for garage {GarageId}.", candidate.GarageId);
            }
        }

        using (var scope = scopeFactory.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IApplicationSettingRepository>();
            var summary = new Dictionary<string, object?>
            {
                ["garageCount"] = candidates.Count,
                ["repairedCount"] = repaired,
                ["failedCount"] = failed,
                ["throughMonth"] = through.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            };
            settings.Add(new ApplicationSetting
            {
                Key = CompletedSettingKey,
                DateValue = through,
                JsonValue = JsonSerializer.Serialize(summary)
            });
            scope.ServiceProvider.GetRequiredService<IAuditEventWriter>().Add(new AuditEventWriteRequest(
                null,
                "finance.accrual_period_repair_completed",
                "maintenance",
                CompletedSettingKey,
                Summary: $"Однократный пересчёт неоплаченных начислений по тарифам своих месяцев: гаражей {candidates.Count}, пересчитано {repaired}, пропущено {failed}.",
                NewValues: summary));
            await settings.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Accrual period repair completed: garages {GarageCount}, repaired {RepairedCount}, failed {FailedCount}.",
            candidates.Count, repaired, failed);
        return new AccrualPeriodRepairResult(false, candidates.Count, repaired, failed);
    }
}

public sealed class AccrualPeriodRepairWorker(
    AccrualPeriodRepairService repairService,
    IOptions<RegularAccrualAutomationOptions> options,
    ILogger<AccrualPeriodRepairWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Disabled together with accrual automation, e.g. on a restored private copy.
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            await repairService.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Accrual period repair did not complete and will be retried after restart.");
        }
    }
}
