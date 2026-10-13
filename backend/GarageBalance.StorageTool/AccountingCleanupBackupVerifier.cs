using System.Security.Cryptography;
using GarageBalance.Api.Application.Maintenance;

namespace GarageBalance.StorageTool;

public sealed class AccountingCleanupBackupVerifier : IAccountingCleanupBackupVerifier
{
    public async Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.FormatVersion != 1 || receipt.Source != source || !receipt.RestoreSucceeded ||
            receipt.RestoredSchemaFingerprint != source.SchemaFingerprint ||
            receipt.BackupCreatedAtUtc > now || receipt.RestoreVerifiedAtUtc > now ||
            receipt.RestoreVerifiedAtUtc < receipt.BackupCreatedAtUtc || now - receipt.BackupCreatedAtUtc > TimeSpan.FromHours(24) ||
            string.IsNullOrWhiteSpace(receipt.Sha256) || receipt.Sha256.Length != 64 || !receipt.Sha256.All(Uri.IsHexDigit))
            throw new AccountingCleanupException("cleanup_backup_proof_invalid", "Нужна свежая проверенная резервная копия той же базы и схемы.");

        if (string.IsNullOrWhiteSpace(receipt.BackupPath) || !Path.IsPathFullyQualified(receipt.BackupPath) || !File.Exists(receipt.BackupPath) ||
            File.GetAttributes(receipt.BackupPath).HasFlag(FileAttributes.ReparsePoint))
            throw new AccountingCleanupException("cleanup_backup_file_invalid", "Файл резервной копии отсутствует или его путь небезопасен.");

        await using var file = new FileStream(receipt.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[5];
        var read = await file.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (read != header.Length || !header.AsSpan().SequenceEqual("PGDMP"u8))
            throw new AccountingCleanupException("cleanup_backup_format_invalid", "Нужна копия PostgreSQL в проверенном custom-формате.");
        file.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (!hash.Equals(receipt.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new AccountingCleanupException("cleanup_backup_hash_mismatch", "Контрольная сумма резервной копии изменилась.");
    }
}
