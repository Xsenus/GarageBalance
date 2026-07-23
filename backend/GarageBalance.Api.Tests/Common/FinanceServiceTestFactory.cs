using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Tests.Common;

internal static class FinanceServiceTestFactory
{
    public static FinanceService Create(GarageBalanceDbContext dbContext, TimeProvider? timeProvider = null) =>
        new(
            new EfStaffMemberRepository(dbContext),
            new EfGarageRepository(dbContext, TestBusinessDateProvider.From(timeProvider)),
            new EfMissingMeterReadingQuery(dbContext),
            new EfGarageIncomeWorksheetQuery(dbContext),
            new EfGarageBalanceHistoryQuery(dbContext),
            new EfFinanceAvailableBalanceQuery(dbContext),
            new EfExpenseWorksheetQuery(dbContext),
            new EfFinancialOperationDisplayQuery(dbContext),
            new EfFinanceTotalsQuery(dbContext),
            new EfFinancialReportPeriodQuery(dbContext),
            new EfMeterReadingRepository(dbContext),
            new EfFinancialOperationRepository(dbContext),
            new EfAccrualRepository(dbContext),
            new EfAccrualPaymentAllocationRepository(dbContext),
            new EfSupplierAccrualRepository(dbContext),
            new EfStaffSalaryAdjustmentRepository(dbContext),
            new EfSupplierGroupRepository(dbContext),
            new EfSupplierRepository(dbContext),
            new EfExpenseTypeRepository(dbContext),
            new EfIncomeTypeRepository(dbContext),
            new EfIrregularPaymentRepository(dbContext),
            new EfTariffRepository(dbContext),
            new EfFeeCampaignRepository(dbContext),
            new EfChargeServiceSettingRepository(dbContext),
            new IncomeFundAssignmentService(
                new EfFundRepository(dbContext),
                new AuditEventWriter(dbContext)),
            new EfApplicationUnitOfWork(dbContext),
            new AuditEventWriter(dbContext),
            timeProvider ?? TimeProvider.System,
            TestBusinessDateProvider.From(timeProvider));
}
