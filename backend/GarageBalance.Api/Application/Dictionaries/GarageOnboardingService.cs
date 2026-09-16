using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class GarageOnboardingService(
    IDictionaryService dictionaryService,
    IFinanceService financeService,
    IGarageOnboardingTransactionRunner transactionRunner,
    IBusinessDateProvider businessDateProvider) : IGarageOnboardingService
{
    public Task<DictionaryResult<GarageDto>> CreateWithAnnualPaymentsAsync(
        CreateGarageWithAnnualPaymentsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return transactionRunner.ExecuteAsync(async transactionCancellationToken =>
        {
            if (request.AccountingYear != businessDateProvider.Today.Year)
            {
                return DictionaryResult<GarageDto>.Failure(
                    "garage_annual_payment_year_invalid",
                    "Погашенные годовые платежи при добавлении гаража можно внести только за текущий год.");
            }

            var requestedPayments = request.AnnualPayments
                .GroupBy(item => item.IncomeTypeId)
                .ToArray();
            if (requestedPayments.Any(group => group.Count() > 1))
            {
                return DictionaryResult<GarageDto>.Failure(
                    "garage_annual_payment_duplicate",
                    "Каждый годовой платёж можно указать только один раз.");
            }

            var garageResult = await dictionaryService.CreateGarageAsync(
                request.Garage,
                actorUserId,
                transactionCancellationToken);
            if (!garageResult.Succeeded)
            {
                return garageResult;
            }

            var annualResult = await financeService.CalculateGarageAnnualPaymentsAsync(
                garageResult.Value!.Id,
                request.AccountingYear,
                actorUserId,
                transactionCancellationToken);
            if (!annualResult.Succeeded)
            {
                return FromFinanceFailure(annualResult);
            }

            var annualItems = annualResult.Value!.Items.ToDictionary(item => item.IncomeTypeId);
            foreach (var payment in request.AnnualPayments)
            {
                var amount = MoneyMath.RoundMoney(payment.Amount);
                if (!annualItems.TryGetValue(payment.IncomeTypeId, out var annualItem) ||
                    annualItem.AccrualId is null ||
                    amount <= 0m ||
                    amount > annualItem.OutstandingAmount)
                {
                    return DictionaryResult<GarageDto>.Failure(
                        "garage_annual_payment_amount_invalid",
                        "Сумма погашенного годового платежа должна быть больше нуля и не превышать полную стоимость тарифа.");
                }

                var incomeResult = await financeService.CreateIncomeAsync(
                    new CreateIncomeOperationRequest(
                        garageResult.Value.Id,
                        annualItem.IncomeTypeId,
                        businessDateProvider.Today,
                        annualItem.AccountingMonth,
                        amount,
                        null,
                        $"Погашенный годовой платёж при добавлении гаража за {request.AccountingYear} год",
                        TargetAccrualId: annualItem.AccrualId),
                    actorUserId,
                    transactionCancellationToken);
                if (!incomeResult.Succeeded)
                {
                    return FromFinanceFailure(incomeResult);
                }
            }

            var refreshedGarage = (await dictionaryService.GetGaragesAsync(
                    garageResult.Value.Number,
                    transactionCancellationToken,
                    limit: 10))
                .SingleOrDefault(item => item.Id == garageResult.Value.Id) ?? garageResult.Value;
            return DictionaryResult<GarageDto>.Success(refreshedGarage);
        }, cancellationToken);
    }

    public Task<DictionaryResult<GarageDto>> UpdateWithAnnualPaymentsAsync(
        Guid garageId,
        UpdateGarageWithAnnualPaymentsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return transactionRunner.ExecuteAsync(async transactionCancellationToken =>
        {
            if (request.AccountingYear != businessDateProvider.Today.Year)
            {
                return DictionaryResult<GarageDto>.Failure(
                    "garage_annual_payment_year_invalid",
                    "Годовые платежи в карточке гаража можно изменять только за текущий год.");
            }

            var requestedPayments = request.AnnualPayments
                .GroupBy(item => item.IncomeTypeId)
                .ToArray();
            if (requestedPayments.Any(group => group.Count() > 1))
            {
                return DictionaryResult<GarageDto>.Failure(
                    "garage_annual_payment_duplicate",
                    "Каждый годовой платёж можно указать только один раз.");
            }

            var garageResult = await dictionaryService.UpdateGarageAsync(
                garageId,
                request.Garage,
                actorUserId,
                transactionCancellationToken);
            if (!garageResult.Succeeded)
            {
                return garageResult;
            }

            var annualResult = await financeService.CalculateGarageAnnualPaymentsAsync(
                garageId,
                request.AccountingYear,
                actorUserId,
                transactionCancellationToken);
            if (!annualResult.Succeeded)
            {
                return FromFinanceFailure(annualResult);
            }

            var annualItems = annualResult.Value!.Items.ToDictionary(item => item.IncomeTypeId);
            foreach (var payment in request.AnnualPayments)
            {
                var requestedTotal = MoneyMath.RoundMoney(payment.Amount);
                if (!annualItems.TryGetValue(payment.IncomeTypeId, out var annualItem) ||
                    annualItem.Amount is null)
                {
                    return DictionaryResult<GarageDto>.Failure(
                        "garage_annual_payment_amount_invalid",
                        "Годовой платёж нельзя сохранить: начисление текущего года не рассчитано.");
                }

                if (requestedTotal < annualItem.PaidAmount)
                {
                    return DictionaryResult<GarageDto>.Failure(
                        "garage_annual_payment_total_decrease",
                        "Уже проведённую сумму годового платежа нельзя уменьшить в карточке гаража.");
                }

                if (requestedTotal > annualItem.Amount.Value)
                {
                    return DictionaryResult<GarageDto>.Failure(
                        "garage_annual_payment_amount_invalid",
                        "Оплаченная сумма годового платежа не может превышать сумму начисления.");
                }

                var amountToRecord = MoneyMath.RoundMoney(requestedTotal - annualItem.PaidAmount);
                if (amountToRecord <= 0m)
                {
                    continue;
                }

                if (annualItem.AccrualId is null || amountToRecord > annualItem.OutstandingAmount)
                {
                    return DictionaryResult<GarageDto>.Failure(
                        "garage_annual_payment_amount_invalid",
                        "Годовой платёж нельзя сохранить: начисление текущего года не рассчитано.");
                }

                var incomeResult = await financeService.CreateIncomeAsync(
                    new CreateIncomeOperationRequest(
                        garageId,
                        annualItem.IncomeTypeId,
                        businessDateProvider.Today,
                        annualItem.AccountingMonth,
                        amountToRecord,
                        null,
                        $"Годовой платёж за {request.AccountingYear} год из карточки гаража",
                        TargetAccrualId: annualItem.AccrualId),
                    actorUserId,
                    transactionCancellationToken);
                if (!incomeResult.Succeeded)
                {
                    return FromFinanceFailure(incomeResult);
                }
            }

            var refreshedGarage = (await dictionaryService.GetGaragesAsync(
                    garageResult.Value!.Number,
                    transactionCancellationToken,
                    limit: 10))
                .SingleOrDefault(item => item.Id == garageId) ?? garageResult.Value;
            return DictionaryResult<GarageDto>.Success(refreshedGarage);
        }, cancellationToken);
    }

    private static DictionaryResult<GarageDto> FromFinanceFailure<TFinance>(FinanceResult<TFinance> result) =>
        DictionaryResult<GarageDto>.Failure(result.ErrorCode ?? "garage_annual_payment_failed", result.ErrorMessage ?? "Не удалось сохранить годовые платежи гаража.");
}
