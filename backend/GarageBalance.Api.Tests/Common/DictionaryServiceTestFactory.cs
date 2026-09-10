using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Tests.Common;

internal static class DictionaryServiceTestFactory
{
    private static readonly DateOnly DefaultBusinessDate = new(2026, 8, 12);

    public static SupplierServiceCatalog CreateSupplierCatalog(GarageBalanceDbContext dbContext) =>
        new(new EfSupplierServiceRepository(dbContext), new EfFundRepository(dbContext),
            new EfApplicationUnitOfWork(dbContext), new AuditEventWriter(dbContext));

    public static DictionaryService Create(
        GarageBalanceDbContext dbContext,
        DateOnly? businessDate = null,
        ITariffAccrualRecalculationService? tariffAccrualRecalculationService = null) =>
        new(
            new EfOwnerRepository(dbContext),
            new EfGarageRepository(dbContext),
            new EfSupplierGroupRepository(dbContext),
            new EfSupplierRepository(dbContext),
            new EfSupplierServiceRepository(dbContext),
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
            tariffAccrualRecalculationService ?? new NoOpTariffAccrualRecalculationService(),
            new EfApplicationUnitOfWork(dbContext),
            new AuditEventWriter(dbContext),
            new TestBusinessDateProvider(businessDate ?? DefaultBusinessDate));

    private sealed class NoOpTariffAccrualRecalculationService : ITariffAccrualRecalculationService
    {
        public Task RecalculateExistingUnpaidAsync(Guid chargeServiceId, Guid incomeTypeId, DateOnly affectedFrom, Guid? actorUserId, string reason, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
