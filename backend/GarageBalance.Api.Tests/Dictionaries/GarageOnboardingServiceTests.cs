using System.Reflection;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageOnboardingServiceTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 16);

    [Fact]
    public async Task CreateWithAnnualPaymentsAsync_CreatesGarageAccrualAndTargetedPaymentInOneTransaction()
    {
        var garage = new GarageDto(Guid.NewGuid(), "ГОД-1", 2, 1, null, null, 0m, null, null, null, false);
        var incomeTypeId = Guid.NewGuid();
        var accrualId = Guid.NewGuid();
        var refreshedGarage = garage with { Balance = -400m };
        var dictionary = Proxy<IDictionaryService>((method, _) => method.Name switch
        {
            nameof(IDictionaryService.CreateGarageAsync) => Task.FromResult(DictionaryResult<GarageDto>.Success(garage)),
            nameof(IDictionaryService.GetGaragesAsync) => Task.FromResult<IReadOnlyList<GarageDto>>([refreshedGarage]),
            _ => throw new InvalidOperationException(method.Name)
        });
        CreateIncomeOperationRequest? capturedPayment = null;
        var finance = Proxy<IFinanceService>((method, args) => method.Name switch
        {
            nameof(IFinanceService.CalculateGarageAnnualPaymentsAsync) => Task.FromResult(FinanceResult<GarageAnnualPaymentsDto>.Success(
                new GarageAnnualPaymentsDto(garage.Id, garage.Number, null, 2026, 900m, 0m, 900m,
                [new GarageAnnualPaymentItemDto(accrualId, incomeTypeId, "Годовая охрана", 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 21), 900m, 0m, 900m, "unpaid", null, null, true, "Охрана 2026")]))),
            nameof(IFinanceService.CreateIncomeAsync) => CaptureIncome(args!, request => capturedPayment = request),
            _ => throw new InvalidOperationException(method.Name)
        });
        var runner = new ImmediateTransactionRunner();
        var service = new GarageOnboardingService(dictionary, finance, runner, new FixedBusinessDateProvider());
        var request = new CreateGarageWithAnnualPaymentsRequest(
            new UpsertGarageRequest(garage.Number, 2, 1, null, 0m, null, null, null),
            2026,
            [new InitialGarageAnnualPaymentRequest(incomeTypeId, 400m)]);

        var result = await service.CreateWithAnnualPaymentsAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Same(refreshedGarage, result.Value);
        Assert.Equal(1, runner.ExecutionCount);
        Assert.NotNull(capturedPayment);
        Assert.Equal(garage.Id, capturedPayment!.GarageId);
        Assert.Equal(incomeTypeId, capturedPayment.IncomeTypeId);
        Assert.Equal(accrualId, capturedPayment.TargetAccrualId);
        Assert.Equal(400m, capturedPayment.Amount);
        Assert.Equal(BusinessDate, capturedPayment.OperationDate);
        Assert.Equal(new DateOnly(2026, 1, 1), capturedPayment.AccountingMonth);
    }

    [Theory]
    [InlineData(2025, 400, "garage_annual_payment_year_invalid")]
    [InlineData(2026, 901, "garage_annual_payment_amount_invalid")]
    public async Task CreateWithAnnualPaymentsAsync_RejectsWrongYearAndAmount(int year, decimal amount, string expectedCode)
    {
        var garage = new GarageDto(Guid.NewGuid(), "ГОД-2", 1, 1, null, null, 0m, null, null, null, false);
        var incomeTypeId = Guid.NewGuid();
        var accrualId = Guid.NewGuid();
        var dictionaryCalls = 0;
        var dictionary = Proxy<IDictionaryService>((method, _) =>
        {
            dictionaryCalls++;
            return Task.FromResult(DictionaryResult<GarageDto>.Success(garage));
        });
        var finance = Proxy<IFinanceService>((method, _) => method.Name switch
        {
            nameof(IFinanceService.CalculateGarageAnnualPaymentsAsync) => Task.FromResult(FinanceResult<GarageAnnualPaymentsDto>.Success(
                new GarageAnnualPaymentsDto(garage.Id, garage.Number, null, 2026, 900m, 0m, 900m,
                [new GarageAnnualPaymentItemDto(accrualId, incomeTypeId, "Взнос", 2026, new DateOnly(2026, 1, 1), BusinessDate, BusinessDate, 900m, 0m, 900m, "unpaid", null, null, true)]))),
            nameof(IFinanceService.CreateIncomeAsync) => throw new InvalidOperationException("Недопустимый платёж не должен сохраняться."),
            _ => throw new InvalidOperationException(method.Name)
        });
        var service = new GarageOnboardingService(dictionary, finance, new ImmediateTransactionRunner(), new FixedBusinessDateProvider());

        var result = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(
                new UpsertGarageRequest(garage.Number, 1, 1, null, 0m, null, null, null),
                year,
                [new InitialGarageAnnualPaymentRequest(incomeTypeId, amount)]),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(year == 2026 ? 1 : 0, dictionaryCalls);
    }

    private static Task<FinanceResult<FinancialOperationDto>> CaptureIncome(
        object?[] args,
        Action<CreateIncomeOperationRequest> capture)
    {
        capture(Assert.IsType<CreateIncomeOperationRequest>(args[0]));
        return Task.FromResult(FinanceResult<FinancialOperationDto>.Success(null!));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private sealed class ImmediateTransactionRunner : IGarageOnboardingTransactionRunner
    {
        public int ExecutionCount { get; private set; }
        public async Task<DictionaryResult<T>> ExecuteAsync<T>(Func<CancellationToken, Task<DictionaryResult<T>>> action, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken);
        }
    }

    private sealed class FixedBusinessDateProvider : IBusinessDateProvider
    {
        public DateOnly SystemDate => BusinessDate;
        public DateOnly Today => BusinessDate;
        public DateOnly? OverrideDate => null;
        public void SetOverride(DateOnly? value) => throw new NotSupportedException();
    }
}
