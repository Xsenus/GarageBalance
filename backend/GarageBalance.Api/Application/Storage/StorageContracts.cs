using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Domain.Storage;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Storage;

public enum StorageMode
{
    Single,
    AsyncMirror
}

public enum StorageProviderType
{
    LocalFileSystem,
    S3Compatible
}

public enum S3EncryptionMode
{
    SseS3,
    SseKms,
    None
}

public enum StorageDestinationState
{
    Enabled,
    ReadOnly,
    Draining,
    Recovering,
    Disabled
}

[Flags]
public enum StorageCapability
{
    None = 0,
    Read = 1,
    Write = 2,
    Stat = 4,
    Delete = 8,
    DownloadLink = 16,
    MultipartUpload = 32,
    ServerSideEncryption = 64
}

public static class S3CredentialSource
{
    private const string EnvironmentPrefix = "EnvironmentVariables:";

    public static bool IsValid(string? source) =>
        source is "DefaultChain" or "EnvironmentOrWorkloadIdentity" || TryGetEnvironmentPrefix(source, out _);

    public static bool TryGetEnvironmentPrefix(string? source, out string prefix)
    {
        prefix = string.Empty;
        if (source is null || !source.StartsWith(EnvironmentPrefix, StringComparison.Ordinal)) return false;
        var candidate = source[EnvironmentPrefix.Length..];
        if (candidate.Length is < 1 or > 32 || candidate[0] is < 'A' or > 'Z' ||
            candidate.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
            return false;
        prefix = candidate;
        return true;
    }
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageMode Mode { get; init; } = StorageMode.Single;
    public string TenantId { get; init; } = "garagebalance";
    public List<StorageDestinationOptions> Destinations { get; init; } = [];
    public List<StoragePoolOptions> Pools { get; init; } = [];
    public List<StoragePolicyOptions> Policies { get; init; } = [];
    public StorageReplicationOptions Replication { get; init; } = new();
}

public sealed class StorageDestinationOptions
{
    public string Id { get; init; } = string.Empty;
    public StorageProviderType Type { get; init; }
    public StorageDestinationState State { get; init; } = StorageDestinationState.Enabled;
    public string FailureDomain { get; init; } = string.Empty;
    public string TenantId { get; init; } = "garagebalance";
    public string? RootPath { get; init; }
    public string? Endpoint { get; init; }
    public string? Bucket { get; init; }
    public string Prefix { get; init; } = "garagebalance";
    public string SigningRegion { get; init; } = "us-east-1";
    public bool ForcePathStyle { get; init; } = true;
    public long MultipartThresholdBytes { get; init; } = 64L * 1024 * 1024;
    public int MultipartPartSizeBytes { get; init; } = 16 * 1024 * 1024;
    public string CredentialSource { get; init; } = "DefaultChain";
    public S3EncryptionMode EncryptionMode { get; init; } = S3EncryptionMode.SseS3;
    public string? KmsKeyId { get; init; }
    public bool PrivateAccess { get; init; } = true;
    public bool EncryptionAtRest { get; init; } = true;
    public bool UnencryptedAtRestAcknowledged { get; init; }
    public bool AllowInsecureLoopbackEndpoint { get; init; }
    public List<string> AllowedEndpointHosts { get; init; } = [];
    public List<string> Capabilities { get; init; } = [];
}

public sealed class StoragePoolOptions
{
    public string Id { get; init; } = string.Empty;
    public List<string> DestinationIds { get; init; } = [];
}

public sealed class StoragePolicyOptions
{
    public string Id { get; init; } = string.Empty;
    public StorageDataClass DataClass { get; init; }
    public string PoolId { get; init; } = string.Empty;
    public int Revision { get; init; } = 1;
    public int RequiredIndependentCopies { get; init; } = 1;
    public int MinimumOffsiteCopies { get; init; }
    public int DesiredCopies { get; init; } = 1;
    public List<string> RequiredCapabilities { get; init; } = ["Read", "Write", "Stat"];
}

public sealed class StorageReplicationOptions
{
    public int PollSeconds { get; init; } = 15;
    public int MaxParallelPerDestination { get; init; } = 2;
    public int OperationDeadlineSeconds { get; init; } = 300;
    public int LeaseSeconds { get; init; } = 120;
    public int MaximumAttempts { get; init; } = 12;
    public long MaximumPendingBytes { get; init; } = 20L * 1024 * 1024 * 1024;
}

public sealed record EffectiveStorageConfiguration(
    StorageMode Mode,
    string TenantId,
    IReadOnlyList<EffectiveStorageDestination> Destinations,
    IReadOnlyList<StoragePoolOptions> Pools,
    IReadOnlyList<StoragePolicyOptions> Policies,
    StorageReplicationOptions Replication);

public sealed record EffectiveStorageDestination(
    string Id,
    StorageProviderType Type,
    StorageDestinationState State,
    string FailureDomain,
    string TenantId,
    string? RootPath,
    Uri? Endpoint,
    string? Bucket,
    string Prefix,
    string SigningRegion,
    bool ForcePathStyle,
    long MultipartThresholdBytes,
    int MultipartPartSizeBytes,
    string CredentialSource,
    StorageCapability Capabilities,
    S3EncryptionMode EncryptionMode = S3EncryptionMode.SseS3,
    string? KmsKeyId = null);

public static partial class StorageObjectKey
{
    public static string Normalize(string value)
    {
        value = value?.Trim().Replace('\\', '/') ?? string.Empty;
        if (value.Length is 0 or > 1024 || value.StartsWith('/') || value.EndsWith('/') ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException("Storage object key must be a relative non-empty path up to 1024 characters.", nameof(value));
        }

        var segments = value.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("Storage object key contains an empty or traversal segment.", nameof(value));
        }

        return string.Join('/', segments);
    }

    internal static bool IsValidId(string value) => StorageIdPattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex StorageIdPattern();
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private static readonly StorageCapability RequiredProviderCapabilities =
        StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat;

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var errors = new List<string>();
        if (!StorageObjectKey.IsValidId(options.TenantId))
        {
            errors.Add("Storage:TenantId must be a lowercase stable identifier.");
        }

        ValidateReplication(options.Replication, errors);
        var destinationIds = new HashSet<string>(StringComparer.Ordinal);
        var effectiveDestinations = new Dictionary<string, (StorageDestinationOptions Options, StorageCapability Capabilities)>(StringComparer.Ordinal);
        foreach (var destination in options.Destinations)
        {
            if (!StorageObjectKey.IsValidId(destination.Id) || !destinationIds.Add(destination.Id))
            {
                errors.Add($"Storage destination id '{destination.Id}' is invalid or duplicated.");
                continue;
            }

            var capabilities = ParseCapabilities(destination.Capabilities, $"destination '{destination.Id}'", errors);
            effectiveDestinations[destination.Id] = (destination, capabilities);
            ValidateDestination(options, destination, capabilities, errors);
        }

        var pools = new Dictionary<string, StoragePoolOptions>(StringComparer.Ordinal);
        foreach (var pool in options.Pools)
        {
            if (!StorageObjectKey.IsValidId(pool.Id) || !pools.TryAdd(pool.Id, pool))
            {
                errors.Add($"Storage pool id '{pool.Id}' is invalid or duplicated.");
                continue;
            }

            if (pool.DestinationIds.Count == 0 || pool.DestinationIds.Distinct(StringComparer.Ordinal).Count() != pool.DestinationIds.Count)
            {
                errors.Add($"Storage pool '{pool.Id}' must contain unique destinations.");
            }

            foreach (var destinationId in pool.DestinationIds)
            {
                if (!effectiveDestinations.ContainsKey(destinationId))
                {
                    errors.Add($"Storage pool '{pool.Id}' references unknown destination '{destinationId}'.");
                }
            }
        }

        var policyIds = new HashSet<string>(StringComparer.Ordinal);
        var dataClasses = new HashSet<StorageDataClass>();
        foreach (var policy in options.Policies)
        {
            if (!StorageObjectKey.IsValidId(policy.Id) || !policyIds.Add(policy.Id))
            {
                errors.Add($"Storage policy id '{policy.Id}' is invalid or duplicated.");
            }
            if (!dataClasses.Add(policy.DataClass))
            {
                errors.Add($"Storage data class '{policy.DataClass}' has more than one policy.");
            }
            ValidatePolicy(policy, pools, effectiveDestinations, errors);
        }

        if (options.Mode == StorageMode.AsyncMirror)
        {
            if (options.Destinations.Count < 2 || options.Pools.Count == 0 || options.Policies.Count == 0)
            {
                errors.Add("Storage AsyncMirror mode requires at least two destinations, one pool, and one policy.");
            }
            if (!dataClasses.Contains(StorageDataClass.DatabaseBackup))
            {
                errors.Add("Storage AsyncMirror mode requires a DatabaseBackup policy.");
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateReplication(StorageReplicationOptions options, List<string> errors)
    {
        if (options.PollSeconds is < 1 or > 3600 ||
            options.MaxParallelPerDestination is < 1 or > 32 ||
            options.OperationDeadlineSeconds is < 10 or > 86400 ||
            options.LeaseSeconds is < 30 or > 3600 ||
            options.MaximumAttempts is < 1 or > 100 ||
            options.MaximumPendingBytes is < 1024 * 1024)
        {
            errors.Add("Storage replication limits are outside the supported ranges.");
        }
    }

    private static void ValidateDestination(
        StorageOptions root,
        StorageDestinationOptions destination,
        StorageCapability capabilities,
        List<string> errors)
    {
        if (!string.Equals(root.TenantId, destination.TenantId, StringComparison.Ordinal))
        {
            errors.Add($"Storage destination '{destination.Id}' belongs to another tenant.");
        }
        if (!StorageObjectKey.IsValidId(destination.FailureDomain))
        {
            errors.Add($"Storage destination '{destination.Id}' requires a stable failure domain.");
        }
        if (!destination.PrivateAccess ||
            !destination.EncryptionAtRest &&
            !(destination.Type == StorageProviderType.S3Compatible && destination.EncryptionMode == S3EncryptionMode.None &&
              destination.UnencryptedAtRestAcknowledged))
        {
            errors.Add($"Storage destination '{destination.Id}' must be private and encrypted at rest.");
        }
        if ((capabilities & RequiredProviderCapabilities) != RequiredProviderCapabilities &&
            destination.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering)
        {
            errors.Add($"Storage destination '{destination.Id}' lacks Read, Write, or Stat capability.");
        }

        if (destination.Type == StorageProviderType.LocalFileSystem)
        {
            if (string.IsNullOrWhiteSpace(destination.RootPath))
            {
                errors.Add($"Local storage destination '{destination.Id}' requires RootPath.");
            }
            return;
        }

        if (!S3CredentialSource.IsValid(destination.CredentialSource))
        {
            errors.Add($"Storage destination '{destination.Id}' has an unsupported non-secret CredentialSource.");
        }
        if (!Enum.IsDefined(destination.EncryptionMode) ||
            destination.EncryptionMode == S3EncryptionMode.SseKms &&
            (string.IsNullOrWhiteSpace(destination.KmsKeyId) || destination.KmsKeyId.Length > 255 ||
             destination.KmsKeyId.Any(char.IsWhiteSpace) || destination.KmsKeyId.Any(char.IsControl)))
        {
            errors.Add($"S3 destination '{destination.Id}' requires a valid KMS key id for its encryption mode.");
        }
        if (string.IsNullOrWhiteSpace(destination.Bucket) || destination.Bucket.Length > 255)
        {
            errors.Add($"S3 destination '{destination.Id}' requires a bucket.");
        }
        if (destination.EncryptionMode == S3EncryptionMode.None &&
            (destination.EncryptionAtRest || !destination.UnencryptedAtRestAcknowledged))
        {
            errors.Add($"S3 destination '{destination.Id}' requires explicit unencrypted-at-rest acknowledgement.");
        }
        if (destination.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering &&
            destination.EncryptionMode != S3EncryptionMode.None &&
            !capabilities.HasFlag(StorageCapability.ServerSideEncryption))
        {
            errors.Add($"S3 destination '{destination.Id}' must support server-side encryption.");
        }
        if (string.IsNullOrWhiteSpace(destination.SigningRegion) || destination.SigningRegion.Length > 64 ||
            destination.SigningRegion.Any(character => !(char.IsLetterOrDigit(character) || character is '-')))
        {
            errors.Add($"S3 destination '{destination.Id}' requires a safe signing region.");
        }
        if (capabilities.HasFlag(StorageCapability.MultipartUpload) &&
            (destination.MultipartPartSizeBytes < 5 * 1024 * 1024 ||
             destination.MultipartThresholdBytes < destination.MultipartPartSizeBytes))
        {
            errors.Add($"S3 destination '{destination.Id}' requires multipart parts of at least 5 MiB and a threshold not smaller than one part.");
        }
        try
        {
            _ = StorageObjectKey.Normalize(destination.Prefix);
        }
        catch (ArgumentException)
        {
            errors.Add($"S3 destination '{destination.Id}' has an invalid prefix.");
        }
        if (!Uri.TryCreate(destination.Endpoint, UriKind.Absolute, out var endpoint) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            errors.Add($"S3 destination '{destination.Id}' requires a safe absolute endpoint.");
            return;
        }
        var loopback = endpoint.IsLoopback || string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        if (endpoint.Scheme != Uri.UriSchemeHttps &&
            !(destination.AllowInsecureLoopbackEndpoint && loopback && endpoint.Scheme == Uri.UriSchemeHttp))
        {
            errors.Add($"S3 destination '{destination.Id}' must use HTTPS; HTTP is allowed only for an explicitly enabled loopback test endpoint.");
        }
        if (destination.AllowedEndpointHosts.Count == 0 ||
            !destination.AllowedEndpointHosts.Contains(endpoint.Host, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"S3 destination '{destination.Id}' endpoint host is not explicitly allowlisted.");
        }
        if (string.Equals(endpoint.Host, "s3.cloud.ru", StringComparison.OrdinalIgnoreCase) &&
            destination.EncryptionMode != S3EncryptionMode.SseKms)
        {
            errors.Add($"S3 destination '{destination.Id}' must use SSE-KMS with Cloud.ru Object Storage.");
        }
    }

    private static void ValidatePolicy(
        StoragePolicyOptions policy,
        IReadOnlyDictionary<string, StoragePoolOptions> pools,
        IReadOnlyDictionary<string, (StorageDestinationOptions Options, StorageCapability Capabilities)> destinations,
        List<string> errors)
    {
        if (!pools.TryGetValue(policy.PoolId, out var pool))
        {
            errors.Add($"Storage policy '{policy.Id}' references unknown pool '{policy.PoolId}'.");
            return;
        }
        if (policy.Revision < 1 || policy.RequiredIndependentCopies < 1 ||
            policy.DesiredCopies < policy.RequiredIndependentCopies ||
            policy.MinimumOffsiteCopies < 0 || policy.MinimumOffsiteCopies > policy.RequiredIndependentCopies ||
            policy.DesiredCopies > pool.DestinationIds.Count)
        {
            errors.Add($"Storage policy '{policy.Id}' has inconsistent copy counts or revision.");
        }

        var requiredCapabilities = ParseCapabilities(policy.RequiredCapabilities, $"policy '{policy.Id}'", errors);
        var eligible = pool.DestinationIds
            .Where(destinations.ContainsKey)
            .Select(id => destinations[id])
            .Where(item => item.Options.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering)
            .Where(item => (item.Capabilities & requiredCapabilities) == requiredCapabilities)
            .ToArray();
        if (eligible.Length < policy.RequiredIndependentCopies)
        {
            errors.Add($"Storage policy '{policy.Id}' cannot meet its required copy count with capable enabled destinations.");
        }
        if (eligible.Select(item => item.Options.FailureDomain).Distinct(StringComparer.Ordinal).Count() < policy.RequiredIndependentCopies)
        {
            errors.Add($"Storage policy '{policy.Id}' does not have enough independent failure domains.");
        }
        var offsiteCount = eligible.Count(item => item.Options.Type != StorageProviderType.LocalFileSystem);
        if (offsiteCount < policy.MinimumOffsiteCopies)
        {
            errors.Add($"Storage policy '{policy.Id}' cannot meet its minimum off-site copy count.");
        }
    }

    internal static StorageCapability ParseCapabilities(IEnumerable<string> values, string owner, List<string> errors)
    {
        var result = StorageCapability.None;
        foreach (var value in values)
        {
            if (!Enum.TryParse<StorageCapability>(value, ignoreCase: true, out var capability) || capability == StorageCapability.None)
            {
                errors.Add($"Storage {owner} contains unsupported capability '{value}'.");
                continue;
            }
            result |= capability;
        }
        return result;
    }
}

public sealed class StorageConfigurationResolver(
    IOptions<StorageOptions> storageOptions,
    IOptions<DatabaseBackupOptions> backupOptions)
{
    public EffectiveStorageConfiguration Resolve()
    {
        var options = storageOptions.Value;
        if (options.Mode == StorageMode.Single && options.Destinations.Count == 0)
        {
            var local = new EffectiveStorageDestination(
                "local-hot",
                StorageProviderType.LocalFileSystem,
                StorageDestinationState.Enabled,
                "local-host",
                options.TenantId,
                backupOptions.Value.Directory,
                null,
                null,
                "garagebalance",
                "us-east-1",
                true,
                64L * 1024 * 1024,
                16 * 1024 * 1024,
                "DefaultChain",
                StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat | StorageCapability.Delete);
            var pool = new StoragePoolOptions { Id = "database-backups", DestinationIds = [local.Id] };
            var policy = new StoragePolicyOptions
            {
                Id = "database-backups",
                DataClass = StorageDataClass.DatabaseBackup,
                PoolId = pool.Id,
                RequiredIndependentCopies = 1,
                DesiredCopies = 1,
                MinimumOffsiteCopies = 0
            };
            return new EffectiveStorageConfiguration(options.Mode, options.TenantId, [local], [pool], [policy], options.Replication);
        }

        var errors = new List<string>();
        var destinations = options.Destinations.Select(item => new EffectiveStorageDestination(
            item.Id,
            item.Type,
            item.State,
            item.FailureDomain,
            item.TenantId,
            item.RootPath,
            Uri.TryCreate(item.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
            item.Bucket,
            StorageObjectKey.Normalize(item.Prefix),
            item.SigningRegion,
            item.ForcePathStyle,
            item.MultipartThresholdBytes,
            item.MultipartPartSizeBytes,
            item.CredentialSource,
            StorageOptionsValidator.ParseCapabilities(item.Capabilities, $"destination '{item.Id}'", errors),
            item.EncryptionMode,
            item.KmsKeyId)).ToArray();
        if (errors.Count > 0)
        {
            throw new OptionsValidationException(StorageOptions.SectionName, typeof(StorageOptions), errors);
        }
        return new EffectiveStorageConfiguration(options.Mode, options.TenantId, destinations, options.Pools, options.Policies, options.Replication);
    }
}

public sealed record StorageWriteRequest(
    Guid OperationId,
    string ObjectKey,
    long Generation,
    long SizeBytes,
    string Sha256,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StorageWriteResult(string NativeLocator, string? ProviderVersionId, string? ProviderChecksum);
public sealed record StorageObjectStat(long SizeBytes, string? ProviderChecksum, string? ProviderVersionId, IReadOnlyDictionary<string, string> Metadata);
public sealed record StorageDownloadLink(Uri Url, DateTimeOffset ExpiresAtUtc);

public interface IStorageRepairProvider : IStorageProvider
{
    Task<StorageWriteResult> RepairAsync(StorageWriteRequest request, Stream content, Guid repairId, CancellationToken cancellationToken);
}

public interface IStorageProvider
{
    string DestinationId { get; }
    StorageCapability Capabilities { get; }
    string GetWriteLocator(StorageWriteRequest request);
    Task<StorageWriteResult> WriteAsync(StorageWriteRequest request, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken);
    Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken);
    Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken);
    Task<StorageDownloadLink?> GetDownloadLinkAsync(string nativeLocator, TimeSpan lifetime, CancellationToken cancellationToken);
}

public interface IStorageProviderRegistry
{
    IStorageProvider GetRequired(string destinationId);
    IReadOnlyList<IStorageProvider> GetAll();
}

public enum StorageErrorCategory
{
    TransientNetwork,
    RateLimited,
    CredentialsExpired,
    ProviderForbidden,
    ObjectMissing,
    QuotaOrReadOnly,
    Conflict,
    ValidationOrUnsupported,
    ChecksumOrStale,
    ArchivePending,
    TlsSecurity,
    Cancelled,
    UnknownOutcome
}

public sealed class StorageProviderException(
    StorageErrorCategory category,
    string safeMessage,
    Exception? innerException = null) : Exception(safeMessage, innerException)
{
    public StorageErrorCategory Category { get; } = category;
}
