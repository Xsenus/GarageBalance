using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface ICashBankTransferRepository
{
    void Add(CashBankTransfer transfer);
}
