using System.Net.NetworkInformation;
using GarageBalance.Api.Application.Maintenance;

namespace GarageBalance.StorageTool;

// Only the known staging service is authorized. Fail closed on unknown host state.
public sealed class StagingOfflineProof : IAccountingCleanupHostEnvironment
{
    public const string Service = "garagebalance-staging.service";
    public const int ListenerPort = 3101;
    private const string Fragment = "/etc/systemd/system/garagebalance-staging.service";
    private const string ProcessPattern = "(^|[/[:space:]])GarageBalance[.]Api([.]dll)?([[:space:]]|$)";

    public static bool IsOffline(string? service, int? suppliedPort, string? userId, int systemctlExit,
        string? systemctlOutput, int processCheckExit, bool? listenerPresent)
    {
        if (service != Service || suppliedPort.HasValue || userId?.Trim() != "0" || systemctlExit != 0 ||
            processCheckExit != 1 || listenerPresent != false || systemctlOutput is null) return false;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in systemctlOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.TrimEnd('\r').Split('=', 2);
            if (pair.Length != 2 || !properties.TryAdd(pair[0], pair[1])) return false;
        }
        return properties.GetValueOrDefault("LoadState") == "loaded" &&
            properties.GetValueOrDefault("ActiveState") == "inactive" &&
            properties.GetValueOrDefault("SubState") == "dead" &&
            properties.GetValueOrDefault("MainPID") == "0" &&
            properties.GetValueOrDefault("ControlGroup") == "" &&
            properties.GetValueOrDefault("FragmentPath") == Fragment;
    }

    public async Task<AccountingCleanupExecutionIdentity> GetIdentityAsync(string? serviceName, int? applicationPort, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || serviceName != Service || applicationPort.HasValue)
            return new(false, false, "staging-proof-unavailable");
        try
        {
            var user = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/id", ["-u"], cancellationToken);
            var unit = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/systemctl", ["show", Service,
                "--property=LoadState", "--property=ActiveState", "--property=SubState", "--property=MainPID",
                "--property=ControlGroup", "--property=FragmentPath", "--all"], cancellationToken);
            var processes = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/pgrep", ["-f", ProcessPattern], cancellationToken);
            var listener = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == ListenerPort);
            var safe = File.Exists(Fragment) && user.ExitCode == 0 && IsOffline(serviceName, applicationPort, user.Output, unit.ExitCode, unit.Output, processes.ExitCode, listener);
            return new(safe, safe, safe ? "host-administrator" : "staging-proof-failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(false, false, "staging-proof-unavailable"); }
    }
}
