using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Tests.Common;

internal static class FinanceServiceTestFactory
{
    public static FinanceService Create(GarageBalanceDbContext dbContext) =>
        new(
            new EfStaffMemberRepository(dbContext),
            new EfGarageRepository(dbContext),
            new EfMissingMeterReadingQuery(dbContext),
            new EfFinanceSectionCountQuery(dbContext),
            new EfMeterReadingRepository(dbContext),
            new EfFinancialOperationRepository(dbContext),
            new EfAccrualRepository(dbContext),
            new EfSupplierAccrualRepository(dbContext),
            new EfSupplierGroupRepository(dbContext),
            new EfSupplierRepository(dbContext),
            new EfExpenseTypeRepository(dbContext),
            new EfIncomeTypeRepository(dbContext),
            new EfTariffRepository(dbContext),
            new EfFeeCampaignRepository(dbContext),
            new EfChargeServiceSettingRepository(dbContext),
            new EfFundRepository(dbContext),
            new EfApplicationUnitOfWork(dbContext),
            new AuditEventWriter(dbContext));
}
