using System.Security.Cryptography;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class AccountingCleanupBackupVerifierTests
{
    private static readonly AccountingCleanupSource Source = new("synthetic", "127.0.0.1", 5433,
        "synthetic_cleanup", "synthetic-database", new string('b', 64));

    [Theory]
    [InlineData("valid", null)]
    [InlineData("source", "cleanup_backup_proof_invalid")]
    [InlineData("failed_restore", "cleanup_backup_proof_invalid")]
    [InlineData("schema", "cleanup_backup_proof_invalid")]
    [InlineData("expired", "cleanup_backup_proof_invalid")]
    [InlineData("future", "cleanup_backup_proof_invalid")]
    [InlineData("restore_before_backup", "cleanup_backup_proof_invalid")]
    [InlineData("hash_format", "cleanup_backup_proof_invalid")]
    [InlineData("hash_mismatch", "cleanup_backup_hash_mismatch")]
    [InlineData("missing", "cleanup_backup_file_invalid")]
    [InlineData("relative", "cleanup_backup_file_invalid")]
    [InlineData("header", "cleanup_backup_format_invalid")]
    [InlineData("empty", "cleanup_backup_format_invalid")]
    public async Task ValidatesSourceAgeRestoreReceiptFormatAndActualFileHash(string scenario, string? expectedCode)
    {
        // This is a synthetic hash/header fixture, not a database restore drill.
        var path = Path.Combine(Path.GetTempPath(), $"garagebalance-cleanup-backup-{Guid.NewGuid():N}.pgdump");
        try
        {
            var bytes = scenario == "empty" ? Array.Empty<byte>() : scenario == "header" ? "WRONG synthetic"u8.ToArray() : "PGDMP synthetic hash fixture"u8.ToArray();
            await File.WriteAllBytesAsync(path, bytes);
            var now = DateTimeOffset.UtcNow;
            var receipt = new AccountingCleanupBackupReceipt(1, Source, path,
                Convert.ToHexString(SHA256.HashData(bytes)), now.AddMinutes(-2), now.AddMinutes(-1), Source.SchemaFingerprint, true);
            receipt = scenario switch
            {
                "source" => receipt with { Source = Source with { DatabaseName = "different" } },
                "failed_restore" => receipt with { RestoreSucceeded = false },
                "schema" => receipt with { RestoredSchemaFingerprint = "different" },
                "expired" => receipt with { BackupCreatedAtUtc = now.AddDays(-2) },
                "future" => receipt with { RestoreVerifiedAtUtc = now.AddMinutes(1) },
                "restore_before_backup" => receipt with { RestoreVerifiedAtUtc = now.AddMinutes(-3) },
                "hash_format" => receipt with { Sha256 = "invalid" },
                "hash_mismatch" => receipt with { Sha256 = new string('c', 64) },
                "missing" => receipt with { BackupPath = path + ".missing" },
                "relative" => receipt with { BackupPath = "relative.pgdump" },
                _ => receipt
            };
            var verifier = new AccountingCleanupBackupVerifier();
            if (expectedCode is null) await verifier.VerifyAsync(receipt, Source, now, CancellationToken.None);
            else
            {
                var exception = await Assert.ThrowsAsync<AccountingCleanupException>(() => verifier.VerifyAsync(receipt, Source, now, CancellationToken.None));
                Assert.Equal(expectedCode, exception.Code);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HonorsCancellationDuringFileVerification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"garagebalance-cleanup-backup-{Guid.NewGuid():N}.pgdump");
        try
        {
            await File.WriteAllBytesAsync(path, "PGDMP synthetic"u8.ToArray());
            var now = DateTimeOffset.UtcNow;
            var receipt = new AccountingCleanupBackupReceipt(1, Source, path, new string('a', 64), now, now, Source.SchemaFingerprint, true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AccountingCleanupBackupVerifier().VerifyAsync(receipt, Source, now, cancellation.Token));
        }
        finally { File.Delete(path); }
    }
}
