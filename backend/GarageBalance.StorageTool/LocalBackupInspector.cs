using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Backups;

namespace GarageBalance.StorageTool;

public sealed record LocalBackupInventory(string FileName, long SizeBytes, string? Sha256,
    bool Verified, bool ManifestMissing, string? Error, DatabaseBackupManifest? Manifest);

public sealed partial class LocalBackupInspector(IBackupCommandRunner runner, IBackupToolLocator locator, string pgRestorePath)
{
    public const int MaximumFiles = 20000;
    public static bool IsManagedName(string fileName) => GetBackupKind(fileName) is not null;

    public async Task<IReadOnlyList<LocalBackupInventory>> InspectAsync(string root, string tenantId,
        StoragePolicyOptions policy, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return [];
        if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new MigrationToolException("Backup root may not be a symlink or reparse point during migration.");
        var paths = Directory.EnumerateFiles(root, "*.pgdump", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal).Take(MaximumFiles + 1).ToArray();
        if (paths.Length > MaximumFiles) throw new MigrationToolException("Inventory exceeds the safe metadata limit; no partial coverage result is accepted.");
        var executable = locator.Resolve(pgRestorePath);
        var result = new List<LocalBackupInventory>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!IsManagedName(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                result.Add(new(info.Name, info.Length, null, false, false, "invalid_name_or_link", null));
                continue;
            }
            if (executable is null)
            {
                result.Add(new(info.Name, info.Length, null, false, false, "pg_restore_unavailable", null));
                continue;
            }
            try
            {
                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, cancellationToken));
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                var toc = await runner.RunAsync(new BackupCommand(executable, ["--list", path], new Dictionary<string, string>()), deadline.Token);
                if (source.Length == 0 || toc.ExitCode != 0)
                {
                    result.Add(new(info.Name, source.Length, hash, false, false, "invalid_archive", null));
                    continue;
                }
                var missing = !File.Exists(path + ".manifest.json");
                DatabaseBackupManifest? manifest;
                if (missing)
                {
                    var identity = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}\n{info.Name}\n{hash}"));
                    manifest = new(2, new Guid(identity.AsSpan(0, 16)), 1, info.Name, source.Length, hash,
                        GetBackupKind(info.Name)!, new DateTimeOffset(info.LastWriteTimeUtc),
                        "legacy-backfill", policy.Id, policy.Revision);
                }
                else
                {
                    var sidecar = new FileInfo(path + ".manifest.json");
                    if (sidecar.Length > 65536 || sidecar.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new JsonException();
                    await using var input = File.OpenRead(sidecar.FullName);
                    manifest = await JsonSerializer.DeserializeAsync<DatabaseBackupManifest>(input, MigrationCheckpointStore.JsonOptions, cancellationToken);
                }
                var valid = manifest is not null && manifest.SchemaVersion == 2 && manifest.BackupId != Guid.Empty &&
                    manifest.Generation > 0 && manifest.FileName == info.Name && manifest.SizeBytes == source.Length &&
                    string.Equals(manifest.Sha256, hash, StringComparison.OrdinalIgnoreCase) && manifest.PolicyRevision > 0 &&
                    Regex.IsMatch(manifest.PolicyId, "^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant);
                result.Add(new(info.Name, source.Length, hash, valid, missing, valid ? null : "manifest_mismatch", manifest));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { result.Add(new(info.Name, info.Length, null, false, false, "archive_check_timeout", null)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            { result.Add(new(info.Name, info.Length, null, false, false, "unreadable_archive_or_manifest", null)); }
        }
        return result;
    }

    [GeneratedRegex("^garagebalance_(manual|automatic|pre_update)_\\d{8}_\\d{6}_\\d{3}\\.pgdump$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedName();

    [GeneratedRegex("^garagebalance_\\d{8}-\\d{4}(?:\\d{2})?\\.pgdump$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyTimestampName();

    [GeneratedRegex("^garagebalance_\\d{8}-\\d{6}_[0-9a-f]{40}-\\d+\\.pgdump$", RegexOptions.CultureInvariant)]
    private static partial Regex DeploymentName();

    [GeneratedRegex("^garagebalance_(?:auth_reset|before_access_transfer(?:_v2)?|before_import_acl)_\\d{8}-\\d{6}\\.pgdump$|^garagebalance_before_manual_entry_\\d{8}_\\d{6}\\.pgdump$", RegexOptions.CultureInvariant)]
    private static partial Regex HistoricalOperationName();

    private static string? GetBackupKind(string fileName)
    {
        var managed = ManagedName().Match(fileName);
        if (managed.Success) return managed.Groups[1].Value;
        if (DeploymentName().IsMatch(fileName)) return "pre_update";
        if (LegacyTimestampName().IsMatch(fileName) || HistoricalOperationName().IsMatch(fileName)) return "manual";
        return null;
    }
}
