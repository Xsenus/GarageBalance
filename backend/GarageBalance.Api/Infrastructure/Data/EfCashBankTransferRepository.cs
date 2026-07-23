using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfCashBankTransferRepository(GarageBalanceDbContext dbContext) : ICashBankTransferRepository
{
    public void Add(CashBankTransfer transfer) => dbContext.CashBankTransfers.Add(transfer);
}
