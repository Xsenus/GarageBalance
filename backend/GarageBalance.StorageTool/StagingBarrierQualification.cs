using System.Security.Cryptography;
using System.Net.NetworkInformation;
using System.Text;

namespace GarageBalance.StorageTool;

// This verifies deployment configuration; it does not by itself authorize SQL.
// The independent gate/marker/offline proof is also required at execution time.
public sealed class StagingBarrierQualification
{
    private static readonly IReadOnlyDictionary<string, string> Pins = new Dictionary<string, string>
    {
        ["/usr/local/bin/garagebalance-maintenance-barrier"] = "barrier",
        ["/usr/local/bin/garagebalance-deploy-apply"] = "deploy",
        ["/usr/local/bin/garagebalance-storage-tool-run"] = "storage",
        ["/usr/local/bin/garagebalance-healthcheck"] = "health",
        ["/etc/systemd/system/garagebalance-staging.service"] = "app-unit",
        ["/etc/systemd/system/garagebalance-healthcheck.service"] = "health-unit"
    };
    private static readonly string[] Properties = ["LoadState", "User", "Type", "FragmentPath", "DropInPaths", "ExecStart",
        "ExecStartPre", "ExecStartPost", "ExecStop", "ExecStopPost", "ExecReload", "ExecCondition", "OnFailure", "OnSuccess", "TriggeredBy"];
    private StagingBarrierQualification() { }

    public static async Task<StagingBarrierQualification> VerifyInstalledAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw Invalid();
        var user = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/id", ["-u"], token);
        var addresses = NetworkInterface.GetAllNetworkInterfaces().SelectMany(i => i.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address.ToString()).ToArray();
        if (user.ExitCode != 0 || user.Output.Trim() != "0" ||
            !StagingTargetBinding.IsAllowed(Environment.MachineName, addresses, "127.0.0.1", 5432, "garagebalance_staging")) throw Invalid();
        foreach (var pin in Pins) await VerifyPinnedFileAsync(pin.Key, pin.Value, token);
        string[] units = [StagingOfflineProof.Service, "garagebalance-healthcheck.service", .. MaintenanceParticipantState.StorageUnits];
        foreach (var unit in units)
        {
            var result = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/systemctl",
                ["show", unit, "--all", .. Properties.Select(p => "--property=" + p)], token);
            if (result.ExitCode != 0 || !IsQualifiedUnit(unit, result.Output)) throw Invalid();
        }
        return new();
    }

    // Deliberately has no fake host/delegate parameter on the authority factory.
    public async Task RevalidateAsync(CancellationToken token) => _ = await VerifyInstalledAsync(token);

    private static async Task VerifyPinnedFileAsync(string path, string resource, CancellationToken token)
    {
        using var handle = LinuxMaintenanceBarrierHost.OpenVerifiedRootFile(path);
        if (RandomAccess.GetLength(handle) is <= 0 or > 256 * 1024) throw Invalid();
        await using var actual = new FileStream(handle, FileAccess.Read);
        await using var embedded = typeof(StagingBarrierQualification).Assembly.GetManifestResourceStream("MaintenancePins." + resource) ?? throw Invalid();
        using var reader = new StreamReader(embedded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var source = (await reader.ReadToEndAsync(token)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var actualHash = await SHA256.HashDataAsync(actual, token);
        if (!CryptographicOperations.FixedTimeEquals(expected, actualHash)) throw Invalid();
    }

    public static bool IsQualifiedUnit(string unit, string output)
    {
        string mode, user, type, fragment;
        string[] allowedDropIns;
        if (unit == StagingOfflineProof.Service)
        {
            (mode, user, type, fragment) = ("app", "garagebalance", "simple", unit);
            allowedDropIns = ["50-cloud-backup.conf", "60-backup-layout.conf", "90-selective-cleanup-pause.conf"];
        }
        else if (unit == "garagebalance-healthcheck.service")
        {
            (mode, user, type, fragment) = ("health", "root", "oneshot", unit);
            allowedDropIns = [];
        }
        else if (MaintenanceParticipantState.IsStorageUnit(unit))
        {
            var command = unit["garagebalance-storage-tool@".Length..^8];
            (mode, user, type, fragment) = ("storage " + command, "garagebalance", "oneshot", "garagebalance-storage-tool@.service");
            allowedDropIns = ["60-backup-layout.conf", "70-maintenance-barrier.conf"];
        }
        else return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('=', 2);
            if (parts.Length != 2 || !Properties.Contains(parts[0], StringComparer.Ordinal) || !values.TryAdd(parts[0], parts[1])) return false;
        }
        // systemctl omits empty arrays of execution-command structures even
        // with --all (confirmed against systemd's typed D-Bus properties).
        foreach (var emptyCommand in Properties.Where(p => p.StartsWith("Exec", StringComparison.Ordinal) && p != "ExecStart"))
            values.TryAdd(emptyCommand, "");
        if (Properties.Any(p => !values.ContainsKey(p)) || values["LoadState"] != "loaded" || values["User"] != user ||
            values["Type"] != type || values["FragmentPath"] != "/etc/systemd/system/" + fragment) return false;
        if (Properties.Where(p => p.StartsWith("Exec", StringComparison.Ordinal) && p != "ExecStart" || p is "OnFailure" or "OnSuccess")
            .Any(p => values[p].Length != 0)) return false;
        var expectedPrefix = "{ path=/usr/local/bin/garagebalance-maintenance-barrier ; argv[]=/usr/local/bin/garagebalance-maintenance-barrier " + mode + " ; ignore_errors=no ; ";
        var commandValue = values["ExecStart"];
        if (!commandValue.StartsWith(expectedPrefix, StringComparison.Ordinal) || !commandValue.EndsWith(" }", StringComparison.Ordinal) ||
            commandValue[expectedPrefix.Length..].Contains("argv[]=", StringComparison.Ordinal) ||
            commandValue[expectedPrefix.Length..^2].IndexOfAny(['{', '}']) >= 0) return false;
        var dropIns = values["DropInPaths"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (dropIns.Distinct(StringComparer.Ordinal).Count() != dropIns.Length || dropIns.Any(p =>
            !allowedDropIns.Any(name => p == "/etc/systemd/system/" + fragment + ".d/" + name))) return false;
        if (mode.StartsWith("storage ", StringComparison.Ordinal) && !dropIns.Contains("/etc/systemd/system/" + fragment + ".d/70-maintenance-barrier.conf", StringComparer.Ordinal)) return false;
        var expectedTrigger = mode == "health" ? "garagebalance-healthcheck.timer" : mode == "storage delta-sync" ? "garagebalance-storage-sync.timer" : "";
        return values["TriggeredBy"] == expectedTrigger;
    }

    private static InvalidOperationException Invalid() => new("maintenance_installed_barrier_unqualified");
}
