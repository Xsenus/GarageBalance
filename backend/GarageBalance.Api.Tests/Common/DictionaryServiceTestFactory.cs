using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Tests.Common;

internal static class DictionaryServiceTestFactory
{
    public static DictionaryService Create(GarageBalanceDbContext dbContext) =>
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
            new EfTariffRepository(dbContext),
            new EfIrregularPaymentRepository(dbContext),
            new EfChargeServiceSettingRepository(dbContext),
            new EfFeeCampaignRepository(dbContext),
            new EfApplicationUnitOfWork(dbContext),
            new AuditEventWriter(dbContext));
}
