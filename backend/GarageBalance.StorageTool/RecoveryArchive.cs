using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace GarageBalance.StorageTool;

public sealed record RecoveryCanary(string ProtectedValue, string Sha256);
public sealed record RecoveryEnvironmentConfiguration(StorageOptions Storage, DatabaseBackupOptions DatabaseBackup,
    string ApplicationVersion, IReadOnlyList<string> AppliedMigrations, string ApplicationName = "GarageBalance");
public sealed record RecoveryArchivePayload(int SchemaVersion, string TenantId, Guid BundleId, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<StorageManifestEntry> Backups, RecoveryCanary Canary, RecoveryEnvironmentConfiguration? Environment = null);
public sealed record RecoveryCopy(string DestinationId, string FailureDomain, string NativeLocator);
public sealed record RecoveryBootstrap(int SchemaVersion, string TenantId, Guid BundleId, DateTimeOffset CreatedAtUtc,
    long SizeBytes, string Sha256, IReadOnlyList<RecoveryCopy> Copies);
public sealed record SignedRecoveryBootstrap(string Payload, string AuthenticationTag);

public static class RecoveryArchive
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    private const string CanaryPurpose = "DisasterRecoveryCanary";
    private static readonly byte[] Magic = "GBRB2"u8.ToArray();
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static byte[] ReadKey(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 1024) throw new InvalidOperationException("A separate base64 recovery key file is required.");
        var key = Convert.FromBase64String(File.ReadAllText(path).Trim());
        if (key.Length != 32) throw new InvalidOperationException("Recovery key must contain exactly 32 random bytes.");
        return key;
    }

    public static RecoveryCanary CreateCanary(string keyRingDirectory)
    {
        var protector = CreateProtector(keyRingDirectory);
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new(protector.Protect(random, CanaryPurpose), Hash(Encoding.UTF8.GetBytes(random)));
    }

    public static void VerifyCanary(string keyRingDirectory, RecoveryCanary canary)
    {
        var plain = CreateProtector(keyRingDirectory).Unprotect(canary.ProtectedValue, CanaryPurpose);
        if (!string.Equals(Hash(Encoding.UTF8.GetBytes(plain)), canary.Sha256, StringComparison.Ordinal))
            throw new CryptographicException("Recovered application keys did not decrypt the existing protected canary.");
    }

    public static byte[] Protect(string keyRingDirectory, RecoveryArchivePayload payload, byte[] key)
    {
        if (payload.SchemaVersion != 2 || payload.Backups.Count > 100_000) throw new InvalidDataException("Recovery inventory is invalid or exceeds its bound.");
        VerifyCanary(keyRingDirectory, payload.Canary);
        using var zipBuffer = new MemoryStream();
        using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            long total;
            using (var entry = zip.CreateEntry("catalog.json").Open())
            using (var bounded = new BoundedArchiveEntryStream(entry, MaximumBytes))
            {
                JsonSerializer.Serialize(bounded, payload, Json);
                total = bounded.Written;
            }
            var files = Directory.EnumerateFiles(keyRingDirectory, "*.xml", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray();
            if (files.Length is 0 or > 1000) throw new InvalidDataException("A bounded application key ring is required.");
            foreach (var path in files)
            {
                var info = new FileInfo(path);
                if (info.LinkTarget is not null || (total += info.Length) > MaximumBytes) throw new InvalidDataException("Recovery archive size or key file type is not allowed.");
                using var source = File.OpenRead(path);
                using var target = zip.CreateEntry("key-ring/" + info.Name).Open();
                source.CopyTo(target);
            }
        }
        if (zipBuffer.Length > MaximumBytes) throw new InvalidDataException("Recovery archive is too large.");
        var plain = zipBuffer.ToArray();
        var output = new byte[33 + plain.Length];
        Magic.CopyTo(output, 0);
        RandomNumberGenerator.Fill(output.AsSpan(5, 12));
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(output.AsSpan(5, 12), plain, output.AsSpan(33), output.AsSpan(17, 16), Magic);
            return output;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static RecoveryArchivePayload Unprotect(byte[] encrypted, byte[] key, string outputDirectory)
    {
        if (encrypted.Length is < 34 or > MaximumBytes + 33 || !encrypted.AsSpan(0, 5).SequenceEqual(Magic))
            throw new InvalidDataException("Recovery bundle header or size is invalid.");
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("Recovery output must not exist.");
        var plain = new byte[encrypted.Length - 33];
        var outputCreated = false;
        try
        {
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(encrypted.AsSpan(5, 12), encrypted.AsSpan(33), encrypted.AsSpan(17, 16), plain, Magic);
            using var zip = new ZipArchive(new MemoryStream(plain), ZipArchiveMode.Read);
            if (zip.Entries.Count is 0 or > 1001 || zip.Entries.Sum(entry => entry.Length) > MaximumBytes)
                throw new InvalidDataException("Recovery archive expands beyond its bound.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (!names.Add(entry.FullName) || entry.FullName != "catalog.json" &&
                    (!entry.FullName.StartsWith("key-ring/", StringComparison.Ordinal) ||
                     Path.GetFileName(entry.FullName) != entry.FullName[9..] || !entry.FullName.EndsWith(".xml", StringComparison.Ordinal) ||
                     entry.FullName.Contains('\\') || entry.FullName.Contains("..", StringComparison.Ordinal)))
                    throw new InvalidDataException("Recovery archive contains an unexpected path.");
            }
            var catalog = zip.GetEntry("catalog.json") ?? throw new InvalidDataException("Recovery catalog is missing.");
            using var catalogStream = catalog.Open();
            var payload = JsonSerializer.Deserialize<RecoveryArchivePayload>(catalogStream, Json) ?? throw new InvalidDataException("Recovery catalog is invalid.");
            if (payload.SchemaVersion != 2 || payload.Backups.Count > 100_000) throw new InvalidDataException("Recovery catalog version or count is invalid.");
            CreatePrivateDirectory(outputDirectory);
            outputCreated = true;
            CreatePrivateDirectory(Path.Combine(outputDirectory, "key-ring"));
            foreach (var entry in zip.Entries.Where(item => item.FullName.StartsWith("key-ring/", StringComparison.Ordinal)))
            {
                using var source = entry.Open();
                using var target = new FileStream(Path.Combine(outputDirectory, "key-ring", entry.Name), FileMode.CreateNew);
                source.CopyTo(target);
            }
            VerifyCanary(Path.Combine(outputDirectory, "key-ring"), payload.Canary);
            return payload;
        }
        catch
        {
            if (outputCreated && Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static SignedRecoveryBootstrap Sign(RecoveryBootstrap bootstrap, byte[] key)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(bootstrap, Json));
        if (payload.Length > 128_000) throw new InvalidDataException("Recovery bootstrap exceeds its supported size.");
        return new(payload, Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))));
    }

    public static RecoveryBootstrap Verify(SignedRecoveryBootstrap signed, byte[] key)
    {
        if (signed.Payload.Length > 128_000 || !CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(signed.AuthenticationTag), HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed.Payload))))
            throw new CryptographicException("Recovery bootstrap authentication failed.");
        var bootstrap = JsonSerializer.Deserialize<RecoveryBootstrap>(Convert.FromBase64String(signed.Payload), Json)
                        ?? throw new InvalidDataException("Recovery bootstrap is invalid.");
        if (bootstrap.SchemaVersion != 2 || bootstrap.SizeBytes is < 34 or > MaximumBytes + 33 || bootstrap.Sha256.Length != 64 || bootstrap.Copies.Count > 100)
            throw new InvalidDataException("Recovery bootstrap limits are invalid.");
        return bootstrap;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static DataProtectionSensitiveDataProtector CreateProtector(string path) =>
        new(DataProtectionProvider.Create(new DirectoryInfo(path), options => options.SetApplicationName("GarageBalance").DisableAutomaticKeyGeneration()));

    private sealed class BoundedArchiveEntryStream(Stream inner, long maximum) : Stream
    {
        public long Written { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Written + buffer.Length > maximum) throw new InvalidDataException("Recovery catalog exceeds its uncompressed size limit.");
            inner.Write(buffer);
            Written += buffer.Length;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
