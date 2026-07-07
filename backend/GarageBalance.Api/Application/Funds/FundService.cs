using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Funds;

public sealed class FundService(GarageBalanceDbContext dbContext, IAuditEventWriter auditEventWriter) : IFundService
{
    private static readonly IReadOnlyList<DefaultFundDefinition> DefaultFunds =
    [
        new("Электроэнергия", 10, true),
        new("Водоснабжение", 20, true),
        new("Вывоз мусора", 30, true),
        new("Наружное освещение", 40, true),
        new("Членские взносы", 50, false),
        new("Целевые взносы", 60, true),
        new("Прочее", 70, false)
    ];

    public async Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultFundsAsync(cancellationToken);
        var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);

        var funds = await dbContext.Funds
            .AsNoTracking()
            .OrderBy(fund => fund.SortOrder)
            .ThenBy(fund => fund.Name)
            .ToListAsync(cancellationToken);

        return funds.Select(fund => ToDto(fund, availableToDistribute)).ToList();
    }

    public async Task<FundResult<FundOperationDto>> CreateOperationAsync(Guid fundId, CreateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var fund = await dbContext.Funds.SingleOrDefaultAsync(item => item.Id == fundId, cancellationToken);
        if (fund is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_not_found", "Фонд не найден.");
        }

        if (!fund.AllowOperations)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_allowed", "По этому фонду операции недоступны.");
        }

        var operationKind = request.OperationKind.Trim();
        if (!FundOperationKinds.IsSupported(operationKind))
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_kind_invalid", "Операция фонда должна быть deposit или withdraw.");
        }

        operationKind = FundOperationKinds.Normalize(operationKind);
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_reason_required", "Укажите причину операции фонда.");
        }

        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var balanceBefore = fund.Balance;
        var balanceAfter = operationKind == FundOperationKinds.Deposit
            ? balanceBefore + amount
            : balanceBefore - amount;
        if (operationKind == FundOperationKinds.Deposit)
        {
            var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            if (amount > availableToDistribute)
            {
                return FundResult<FundOperationDto>.Failure(
                    "fund_distribution_amount_exceeded",
                    $"Сумма пополнения не может превышать доступную к распределению сумму {availableToDistribute:0.00} руб.");
            }
        }

        if (balanceAfter < 0)
        {
            return FundResult<FundOperationDto>.Failure("fund_balance_insufficient", "Нельзя изъять из фонда больше собранной суммы.");
        }

        var operation = new FundOperation
        {
            FundId = fund.Id,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Reason = reason,
            ActorUserId = actorUserId
        };

        fund.Balance = balanceAfter;
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
        dbContext.FundOperations.Add(operation);
        AddAudit(fund, operation, actorUserId);
        await dbContext.SaveChangesAsync(cancellationToken);

        operation.Fund = fund;
        return FundResult<FundOperationDto>.Success(ToDto(operation));
    }

    private async Task EnsureDefaultFundsAsync(CancellationToken cancellationToken)
    {
        var existingNames = await dbContext.Funds
            .Select(fund => fund.NormalizedName)
            .ToListAsync(cancellationToken);
        var existing = existingNames.ToHashSet(StringComparer.Ordinal);

        foreach (var definition in DefaultFunds)
        {
            var normalizedName = NormalizeName(definition.Name);
            if (existing.Contains(normalizedName))
            {
                continue;
            }

            dbContext.Funds.Add(new Fund
            {
                Name = definition.Name,
                NormalizedName = normalizedName,
                SortOrder = definition.SortOrder,
                AllowOperations = definition.AllowOperations,
                IsSystem = true
            });
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<decimal> CalculateAvailableToDistributeAsync(CancellationToken cancellationToken)
    {
        var incomeTotal = await dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        var expenseTotal = await dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        var allocatedFundTotal = await dbContext.Funds
            .AsNoTracking()
            .SumAsync(fund => (decimal?)fund.Balance, cancellationToken) ?? 0m;

        return MoneyMath.RoundMoney(Math.Max(incomeTotal - expenseTotal - allocatedFundTotal, 0m));
    }

    private void AddAudit(Fund fund, FundOperation operation, Guid? actorUserId)
    {
        var actionLabel = operation.OperationKind == FundOperationKinds.Deposit ? "Пополнен" : "Выполнено изъятие из";
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: operation.OperationKind == FundOperationKinds.Deposit ? "fund.operation_deposited" : "fund.operation_withdrawn",
            EntityType: "fund_operation",
            EntityId: operation.Id.ToString(),
            Summary: $"{actionLabel} фонда {fund.Name} на сумму {operation.Amount:0.##} руб.",
            Section: "funds",
            ActionKind: "create",
            EntityDisplayName: fund.Name,
            Reason: operation.Reason,
            OldValues: new Dictionary<string, object?>
            {
                ["balance"] = operation.BalanceBefore
            },
            NewValues: new Dictionary<string, object?>
            {
                ["balance"] = operation.BalanceAfter
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["balance"] = "Собранная сумма"
            },
            Metadata: new Dictionary<string, object?>
            {
                ["fundId"] = fund.Id,
                ["fundName"] = fund.Name,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = operation.Amount
            }));
    }

    private static FundDto ToDto(Fund fund, decimal availableToDistribute)
    {
        return new FundDto(fund.Id, fund.Name, fund.Balance, availableToDistribute, fund.SortOrder, fund.AllowOperations, fund.IsSystem);
    }

    private static FundOperationDto ToDto(FundOperation operation)
    {
        return new FundOperationDto(
            operation.Id,
            operation.FundId,
            operation.Fund.Name,
            operation.OperationKind,
            operation.Amount,
            operation.BalanceBefore,
            operation.BalanceAfter,
            operation.Reason,
            operation.CreatedAtUtc);
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToUpperInvariant();
    }

    private sealed record DefaultFundDefinition(string Name, int SortOrder, bool AllowOperations);
}
