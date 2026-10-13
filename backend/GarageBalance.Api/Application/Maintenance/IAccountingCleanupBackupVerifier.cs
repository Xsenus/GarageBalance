namespace GarageBalance.Api.Application.Maintenance;

public interface IAccountingCleanupBackupVerifier
{
    Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source,
        DateTimeOffset now, CancellationToken cancellationToken);
}
