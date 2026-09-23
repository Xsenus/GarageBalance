using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GarageBalance.StorageTool;

public static class DisasterRecoveryCommands
{
    public static bool CanHandle(string command) => command is "recovery-canary" or "recovery-publish" or "recovery-fetch" or "restore-drill";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!args.Contains("--execute", StringComparer.Ordinal))
        {
            Console.Error.WriteLine("Recovery commands are dry-run guarded. Review configuration, then supply --execute.");
            return 3;
        }
        try
        {
            var command = args[0];
            if (command == "recovery-canary")
            {
                var path = Value(args, "--canary");
                if (File.Exists(path)) throw new IOException("An existing canary must be preserved, not replaced.");
                await WriteJsonAtomicAsync(path, RecoveryArchive.CreateCanary(Value(args, "--key-ring")), cancellationToken);
                Console.WriteLine("recoveryCanaryCreated=True");
                return 0;
            }
            var options = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new();
            var validation = new StorageOptionsValidator().Validate(null, options);
            if (validation.Failed) throw new InvalidOperationException("Storage configuration validation failed.");
            var resolver = new StorageConfigurationResolver(Options.Create(options), Options.Create(new DatabaseBackupOptions()));
            var effective = resolver.Resolve();
            using var registry = new StorageProviderRegistry(resolver, new AwsS3ObjectClientFactory(), TimeProvider.System);
            var keyFile = Value(args, "--key-file");
            var key = RecoveryArchive.ReadKey(keyFile);
            try
            {
                if (command == "recovery-publish")
                    return await PublishAsync(args, configuration, effective, registry, key, keyFile, cancellationToken);
                if (command == "restore-drill")
                    return await RestoreDrill.RunAsync(args, configuration, effective, registry, key, cancellationToken);
                var output = Value(args, "--output");
                if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Recovery output must not already exist.");
                var payload = await FetchArchiveAsync(args, effective, registry, key, output, cancellationToken);
                try
                {
                    await WriteJsonAtomicAsync(Path.Combine(output, "catalog.json"), payload, cancellationToken);
                    if (payload.Environment is { } environment)
                        await WriteJsonAtomicAsync(Path.Combine(output, "appsettings.Recovery.json"), environment, cancellationToken);
                }
                catch
                {
                    // This output was created by the successful decrypt above, never supplied as an existing directory.
                    if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
                    throw;
                }
                Console.WriteLine("recoveryBundleAndCanaryVerified=True");
                return 0;
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Recovery operation cancelled."); return 130; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Provider/SQL/crypto diagnostics may contain credentials, paths or private locators.
            Console.Error.WriteLine($"Recovery operation failed ({exception.GetType().Name}); no production restore was attempted.");
            return 20;
        }
    }

    private static async Task<int> PublishAsync(string[] args, IConfiguration configuration, EffectiveStorageConfiguration effective,
        IStorageProviderRegistry registry, byte[] key, string keyFile, CancellationToken ct)
    {
        var policy = effective.Policies.SingleOrDefault(item => item.DataClass == StorageDataClass.RecoverySecrets)
                     ?? throw new InvalidOperationException("An explicit separate RecoverySecrets policy is required.");
        var pool = effective.Pools.Single(item => item.Id == policy.PoolId);
        var targets = pool.DestinationIds.Select(id => effective.Destinations.Single(item => item.Id == id))
            .Where(item => item.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering).ToArray();
        ValidateRecoveryTargets(policy, targets);
        var keyRing = Path.GetFullPath(Value(args, "--key-ring"));
        var output = Path.GetFullPath(Value(args, "--bootstrap"));
        var keyPath = Path.GetFullPath(keyFile);
        if (keyPath.StartsWith(keyRing + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Path.GetDirectoryName(keyPath) == Path.GetDirectoryName(output) || targets.Any(item => item.RootPath is { } root &&
                keyPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The recovery encryption key must be kept independently.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using var publicationLease = new FileStream(output + ".publish.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var canary = JsonSerializer.Deserialize<RecoveryCanary>(await File.ReadAllTextAsync(Value(args, "--canary"), ct), RecoveryArchive.Json)
                     ?? throw new InvalidDataException("An existing protected canary is required.");
        var connection = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Catalog export requires the source database connection.");
        await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);
        var catalog = new EfStorageCatalog(db);
        var entries = new List<StorageManifestEntry>();
        string? cursor = null;
        do
        {
            var page = await catalog.ExportManifestPageAsync(effective.TenantId, StorageDataClass.DatabaseBackup, 250, cursor, ct);
            entries.AddRange(page.Items);
            if (entries.Count > 100_000) throw new InvalidDataException("Recovery inventory exceeds its configured safe limit.");
            cursor = page.NextCursor;
        } while (cursor is not null);
        if (entries.Count == 0) throw new InvalidDataException("An empty backup inventory cannot establish disaster recovery.");
        var bundleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        // Bound typed options have no connection strings, credentials or arbitrary configuration keys.
        var environment = new RecoveryEnvironmentConfiguration(
            configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()!,
            configuration.GetSection(DatabaseBackupOptions.SectionName).Get<DatabaseBackupOptions>() ?? new(),
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                typeof(GarageBalanceDbContext).Assembly)?.InformationalVersion ?? typeof(GarageBalanceDbContext).Assembly.GetName().Version?.ToString() ?? "unknown",
            (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray());
        var encrypted = RecoveryArchive.Protect(keyRing, new(2, effective.TenantId, bundleId, now, entries, canary, environment), key);
        var sha = RecoveryArchive.Hash(encrypted);
        var request = new StorageWriteRequest(bundleId, $"{effective.TenantId}/recovery/{bundleId:N}.gbrb", 1, encrypted.Length, sha,
            new Dictionary<string, string> { ["data-class"] = "RecoverySecrets", ["sha256"] = sha, ["generation"] = "1" });
        var copies = new List<RecoveryCopy>();
        // Partial publication must never replace the last known good bootstrap during an outage.
        var pendingOutput = output + ".pending.json";
        await SaveBootstrapAsync(pendingOutput, effective.TenantId, bundleId, now, encrypted, copies, key, ct);
        foreach (var target in targets)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(effective.Replication.OperationDeadlineSeconds));
            try
            {
                var provider = registry.GetRequired(target.Id);
                var locator = provider.GetWriteLocator(request);
                try { locator = (await provider.WriteAsync(request, new MemoryStream(encrypted, writable: false), deadline.Token)).NativeLocator; }
                catch (StorageProviderException exception) when (exception.Category is StorageErrorCategory.UnknownOutcome or StorageErrorCategory.Conflict) { }
                var verified = await ReadBoundedAsync(provider, locator, encrypted.Length, sha, deadline.Token);
                CryptographicOperations.ZeroMemory(verified);
                copies.Add(new(target.Id, target.FailureDomain, locator));
                await SaveBootstrapAsync(pendingOutput, effective.TenantId, bundleId, now, encrypted, copies, key, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (StorageProviderException) { }
            catch (InvalidDataException) { }
            catch (IOException) { }
        }
        var sufficient = copies.Select(item => item.FailureDomain).Distinct(StringComparer.Ordinal).Count() >= Math.Max(2, policy.RequiredIndependentCopies) &&
                         copies.Where(item => targets.Single(target => target.Id == item.DestinationId).Type != StorageProviderType.LocalFileSystem)
                             .Select(item => item.FailureDomain).Distinct(StringComparer.Ordinal).Count() >= policy.MinimumOffsiteCopies;
        if (sufficient)
        {
            // Prove the complete journal is durable; an earlier provider catch must not hide a journal I/O failure.
            await SaveBootstrapAsync(pendingOutput, effective.TenantId, bundleId, now, encrypted, copies, key, ct);
            File.Move(pendingOutput, output, overwrite: true);
        }
        Console.WriteLine($"recoveryIndependentCopies={copies.Select(item => item.FailureDomain).Distinct().Count()}");
        Console.WriteLine($"recoveryProtectionComplete={sufficient}");
        return sufficient ? 0 : 21;
    }

    public static void ValidateRecoveryTargets(StoragePolicyOptions policy, IReadOnlyList<EffectiveStorageDestination> targets)
    {
        if (policy.DataClass != StorageDataClass.RecoverySecrets || policy.RequiredIndependentCopies < 2 || targets.Count > 100 ||
            targets.Select(item => item.FailureDomain).Distinct(StringComparer.Ordinal).Count() < policy.RequiredIndependentCopies ||
            targets.Any(item => !item.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat)))
            throw new InvalidOperationException("RecoverySecrets requires at least two independent readable writable destinations.");
    }

    internal static async Task<RecoveryArchivePayload> FetchArchiveAsync(string[] args, EffectiveStorageConfiguration effective,
        IStorageProviderRegistry registry, byte[] key, string output, CancellationToken ct)
    {
        var bootstrapInfo = new FileInfo(Value(args, "--bootstrap"));
        if (!bootstrapInfo.Exists || bootstrapInfo.Length > 256_000) throw new InvalidDataException("Recovery bootstrap is missing or oversized.");
        var signed = JsonSerializer.Deserialize<SignedRecoveryBootstrap>(await File.ReadAllTextAsync(bootstrapInfo.FullName, ct), RecoveryArchive.Json)
                     ?? throw new InvalidDataException("Recovery bootstrap is invalid.");
        var bootstrap = RecoveryArchive.Verify(signed, key);
        if (bootstrap.TenantId != effective.TenantId) throw new InvalidDataException("Recovery bootstrap belongs to another tenant.");
        foreach (var copy in bootstrap.Copies)
        {
            var destination = effective.Destinations.SingleOrDefault(item => item.Id == copy.DestinationId);
            if (destination is null || destination.State == StorageDestinationState.Disabled || destination.FailureDomain != copy.FailureDomain) continue;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(effective.Replication.OperationDeadlineSeconds));
            byte[] encrypted;
            try { encrypted = await ReadBoundedAsync(registry.GetRequired(copy.DestinationId), copy.NativeLocator, bootstrap.SizeBytes, bootstrap.Sha256, deadline.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
            catch (StorageProviderException) { continue; }
            catch (InvalidDataException) { continue; }
            catch (IOException) { continue; }
            var payload = RecoveryArchive.Unprotect(encrypted, key, output);
            if (payload.TenantId != bootstrap.TenantId || payload.BundleId != bootstrap.BundleId)
            {
                if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
                throw new InvalidDataException("Recovery inventory identity differs from the authenticated bootstrap.");
            }
            return payload;
        }
        throw new IOException("No verified recovery bundle is available.");
    }

    public static async Task<byte[]> ReadBoundedAsync(IStorageProvider provider, string locator, long expectedSize, string expectedSha, CancellationToken ct)
    {
        if (expectedSize is <= 0 or > RecoveryArchive.MaximumBytes + 33) throw new InvalidDataException("Recovery artifact exceeds its bound.");
        await using var source = await provider.OpenReadAsync(locator, ct);
        using var memory = new MemoryStream((int)expectedSize);
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + count > expectedSize) throw new InvalidDataException("Recovery artifact exceeds committed size.");
            await memory.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var bytes = memory.ToArray();
        if (bytes.Length != expectedSize || !string.Equals(RecoveryArchive.Hash(bytes), expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery artifact checksum or size does not match.");
        return bytes;
    }

    internal static string Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1] : throw new ArgumentException($"Required argument {name} was not supplied.");
    }

    internal static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, RecoveryArchive.Json), ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static Task SaveBootstrapAsync(string path, string tenant, Guid id, DateTimeOffset created, byte[] bytes,
        IReadOnlyList<RecoveryCopy> copies, byte[] key, CancellationToken ct) => WriteJsonAtomicAsync(path,
        RecoveryArchive.Sign(new(2, tenant, id, created, bytes.Length, RecoveryArchive.Hash(bytes), copies), key), ct);
}
