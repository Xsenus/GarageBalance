using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Tests.Common;

internal static class DictionaryServiceTestFactory
{
    private static readonly DateOnly DefaultBusinessDate = new(2026, 8, 12);

    public static DictionaryService Create(GarageBalanceDbContext dbContext, DateOnly? businessDate = null) =>
        new(
            new EfOwnerRepository(dbContext),
            new EfGarageRepository(dbContext),
            new EfSupplierGroupRepository(dbContext),
            new EfSupplierRepository(dbContext),
            new EfSupplierContactRepository(dbContext),
            new EfStaffDepartmentRepository(dbContext),
            new EfStaffMemberRepository(dbContext),
            new EfIncomeTypeRepository(dbContext),
            new EfExpenseTypeRepository(dbContext),
            new EfMeasurementUnitRepository(dbContext),
            new EfTariffRepository(dbContext),
            new EfIrregularPaymentRepository(dbContext),
            new EfChargeServiceSettingRepository(dbContext),
            new EfFeeCampaignRepository(dbContext),
            new EfFundRepository(dbContext),
            new EfOpeningBalanceAdjustmentRepository(dbContext),
            new EfAccrualPaymentAllocationRepository(dbContext),
            new EfApplicationUnitOfWork(dbContext),
            new AuditEventWriter(dbContext),
            new TestBusinessDateProvider(businessDate ?? DefaultBusinessDate));
}
