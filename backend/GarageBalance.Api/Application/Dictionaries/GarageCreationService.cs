using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class GarageCreationService(
    IDictionaryService dictionaryService,
    IFinanceService financeService,
    IExpenseBatchTransactionRunner transactionRunner,
    IBusinessDateProvider businessDateProvider) : IGarageCreationService
{
    public async Task<DictionaryResult<GarageDto>> CreateWithAnnualPaymentsAsync(
        CreateGarageWithAnnualPaymentsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Garage is null || request.AnnualPayments is null || request.AnnualPayments.Count == 0)
        {
            return DictionaryResult<GarageDto>.Failure(
                "garage_annual_payments_required",
                "Укажите гараж и хотя бы один уже оплаченный годовой платёж.");
        }

        if (request.AnnualPayments.Count > 100 ||
            request.AnnualPayments.Any(item => item.IncomeTypeId == Guid.Empty || item.Amount <= 0m || item.Amount > 999999999m))
        {
            return DictionaryResult<GarageDto>.Failure(
                "garage_annual_payments_invalid",
                "Суммы уже оплаченных годовых платежей указаны неверно.");
        }

        if (request.AnnualPayments.Select(item => item.IncomeTypeId).Distinct().Count() != request.AnnualPayments.Count)
        {
            return DictionaryResult<GarageDto>.Failure(
                "garage_annual_payment_duplicate",
                "Один годовой платёж нельзя указать несколько раз.");
        }

        var year = businessDateProvider.Today.Year;
        var preview = await financeService.PreviewGarageAnnualPaymentsAsync(
            new PreviewGarageAnnualPaymentsRequest(year, request.Garage.PeopleCount, request.Garage.FloorCount),
            cancellationToken);
        if (!preview.Succeeded)
        {
            return DictionaryResult<GarageDto>.Failure(preview.ErrorCode!, preview.ErrorMessage!);
        }

        var options = preview.Value!.Items.ToDictionary(item => item.IncomeTypeId);
        foreach (var payment in request.AnnualPayments)
        {
            if (!options.TryGetValue(payment.IncomeTypeId, out var option) || !option.CanRecordPayment || option.Amount is null)
            {
                return DictionaryResult<GarageDto>.Failure(
                    "garage_annual_payment_not_available",
                    "Выбранный годовой платёж нельзя провести для нового гаража.");
            }

            if (MoneyMath.RoundMoney(payment.Amount) > option.Amount.Value)
            {
                return DictionaryResult<GarageDto>.Failure(
                    "garage_annual_payment_amount_exceeds_cost",
                    $"Сумма платежа «{option.ServiceName}» не может превышать полную стоимость {option.Amount.Value:0.00}.");
            }
        }

        var result = await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
        {
            var created = await dictionaryService.CreateGarageAsync(
                request.Garage,
                actorUserId,
                transactionCancellationToken);
            if (!created.Succeeded)
            {
                return FinanceResult<GarageDto>.Failure(created.ErrorCode!, created.ErrorMessage!);
            }

            var annualPayments = await financeService.CalculateGarageAnnualPaymentsAsync(
                created.Value!.Id,
                year,
                actorUserId,
                transactionCancellationToken);
            if (!annualPayments.Succeeded)
            {
                return FinanceResult<GarageDto>.Failure(annualPayments.ErrorCode!, annualPayments.ErrorMessage!);
            }

            foreach (var payment in request.AnnualPayments)
            {
                var annualItem = annualPayments.Value!.Items.SingleOrDefault(item =>
                    item.IncomeTypeId == payment.IncomeTypeId && item.AccrualId.HasValue && item.CanRecordPayment);
                if (annualItem is null)
                {
                    return FinanceResult<GarageDto>.Failure(
                        "garage_annual_payment_accrual_unavailable",
                        "Не удалось создать начисление для выбранного годового платежа.");
                }

                var income = await financeService.CreateIncomeAsync(
                    new CreateIncomeOperationRequest(
                        created.Value.Id,
                        payment.IncomeTypeId,
                        businessDateProvider.Today,
                        new DateOnly(year, businessDateProvider.Today.Month, 1),
                        MoneyMath.RoundMoney(payment.Amount),
                        null,
                        "Уже оплаченный годовой платёж, указанный при создании гаража",
                        TargetAccrualId: annualItem.AccrualId),
                    actorUserId,
                    transactionCancellationToken);
                if (!income.Succeeded)
                {
                    return FinanceResult<GarageDto>.Failure(income.ErrorCode!, income.ErrorMessage!);
                }
            }

            var refreshedGarage = (await dictionaryService.GetGaragesAsync(
                    created.Value.Number,
                    transactionCancellationToken,
                    limit: 10))
                .SingleOrDefault(item => item.Id == created.Value.Id) ?? created.Value;
            return FinanceResult<GarageDto>.Success(refreshedGarage);
        }, cancellationToken);

        return result.Succeeded
            ? DictionaryResult<GarageDto>.Success(result.Value!)
            : DictionaryResult<GarageDto>.Failure(result.ErrorCode!, result.ErrorMessage!);
    }
}
