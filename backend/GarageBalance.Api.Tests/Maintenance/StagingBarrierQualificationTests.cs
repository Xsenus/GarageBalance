using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class StagingBarrierQualificationTests
{
    [Theory]
    [InlineData("app")]
    [InlineData("health")]
    [InlineData("delta-sync")]
    [InlineData("copy")]
    public void RequiresOneEffectiveWrapperAndKnownUnitMetadata(string mode)
    {
        var (unit, values) = Snapshot(mode);
        Assert.True(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        values["ExecStart"] = values["ExecStart"].Replace("start_time=[n/a]", "start_time=[Tue 2026-11-03 14:00:00 MSK]", StringComparison.Ordinal);
        Assert.True(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
    }

    [Theory]
    [InlineData("LoadState", "not-found")]
    [InlineData("User", "other")]
    [InlineData("Type", "forking")]
    [InlineData("FragmentPath", "/tmp/garagebalance-staging.service")]
    [InlineData("DropInPaths", "/etc/systemd/system/garagebalance-staging.service.d/99-unreviewed.conf")]
    [InlineData("ExecStartPre", "unsafe")]
    [InlineData("ExecStartPost", "unsafe")]
    [InlineData("ExecStop", "unsafe")]
    [InlineData("ExecStopPost", "unsafe")]
    [InlineData("ExecReload", "unsafe")]
    [InlineData("ExecCondition", "unsafe")]
    [InlineData("OnFailure", "unsafe.service")]
    [InlineData("OnSuccess", "unsafe.service")]
    [InlineData("TriggeredBy", "unsafe.timer")]
    [InlineData("ExecStart", "")]
    [InlineData("ExecStart", "/opt/garagebalance-staging/api/GarageBalance.Api")]
    public void RejectsUnexpectedCommandsOwnershipAndStartupChannels(string property, string value)
    {
        var (unit, values) = Snapshot("app");
        values[property] = value;
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
    }

    [Fact]
    public void RejectsMalformedMissingDuplicateUnknownAndMultipleCommands()
    {
        var (unit, values) = Snapshot("app");
        var good = Encode(values);
        Assert.False(StagingBarrierQualification.IsQualifiedUnit("other.service", good));
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, good + "\nbroken"));
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, good + "\nUser=garagebalance"));
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, good + "\nUnknown=value"));
        values.Remove("User");
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        (unit, values) = Snapshot("app");
        foreach (var key in values.Keys.Where(p => p.StartsWith("Exec", StringComparison.Ordinal) && p != "ExecStart").ToArray()) values.Remove(key);
        Assert.True(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        (unit, values) = Snapshot("app");
        values["ExecStart"] += " { path=/usr/bin/true ; argv[]=/usr/bin/true ; }";
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        (unit, values) = Snapshot("app");
        values["ExecStart"] = values["ExecStart"].Replace("pid=0", "}  { path=/usr/bin/true", StringComparison.Ordinal);
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
    }

    [Fact]
    public void OnlyReviewedDropInsAreAcceptedAndStorageMustHaveBarrierDropIn()
    {
        var (unit, values) = Snapshot("app");
        values["DropInPaths"] = "/etc/systemd/system/garagebalance-staging.service.d/50-cloud-backup.conf /etc/systemd/system/garagebalance-staging.service.d/60-backup-layout.conf /etc/systemd/system/garagebalance-staging.service.d/90-selective-cleanup-pause.conf";
        Assert.True(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        values["DropInPaths"] += " /etc/systemd/system/garagebalance-staging.service.d/50-cloud-backup.conf";
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        (unit, values) = Snapshot("health");
        values["DropInPaths"] = "/etc/systemd/system/garagebalance-healthcheck.service.d/50-cloud-backup.conf";
        Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        foreach (var name in MaintenanceParticipantState.StorageUnits)
        {
            var command = name["garagebalance-storage-tool@".Length..^8];
            (unit, values) = Snapshot(command);
            Assert.True(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
            values["DropInPaths"] = "";
            Assert.False(StagingBarrierQualification.IsQualifiedUnit(unit, Encode(values)));
        }
    }

    [Fact]
    public async Task PlatformGuardCannotIssueAuthorityOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("maintenance_installed_barrier_unqualified", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StagingBarrierQualification.VerifyInstalledAsync(default))).Message);
    }

    private static (string Unit, Dictionary<string, string> Values) Snapshot(string mode)
    {
        var unit = mode switch { "app" => StagingOfflineProof.Service, "health" => "garagebalance-healthcheck.service", _ => "garagebalance-storage-tool@" + mode + ".service" };
        var storage = mode is not ("app" or "health");
        var effectiveMode = storage ? "storage " + mode : mode;
        return (unit, new()
        {
            ["LoadState"] = "loaded",
            ["User"] = mode == "health" ? "root" : "garagebalance",
            ["Type"] = mode == "app" ? "simple" : "oneshot",
            ["FragmentPath"] = "/etc/systemd/system/" + (storage ? "garagebalance-storage-tool@.service" : unit),
            ["DropInPaths"] = storage ? "/etc/systemd/system/garagebalance-storage-tool@.service.d/60-backup-layout.conf /etc/systemd/system/garagebalance-storage-tool@.service.d/70-maintenance-barrier.conf" : "",
            ["ExecStart"] = "{ path=/usr/local/bin/garagebalance-maintenance-barrier ; argv[]=/usr/local/bin/garagebalance-maintenance-barrier " + effectiveMode + " ; ignore_errors=no ; start_time=[n/a] ; stop_time=[n/a] ; pid=0 ; code=(null) ; status=0/0 }",
            ["ExecStartPre"] = "",
            ["ExecStartPost"] = "",
            ["ExecStop"] = "",
            ["ExecStopPost"] = "",
            ["ExecReload"] = "",
            ["ExecCondition"] = "",
            ["OnFailure"] = "",
            ["OnSuccess"] = "",
            ["TriggeredBy"] = mode == "health" ? "garagebalance-healthcheck.timer" : mode == "delta-sync" ? "garagebalance-storage-sync.timer" : ""
        });
    }
    private static string Encode(Dictionary<string, string> values) => string.Join('\n', values.Select(pair => pair.Key + "=" + pair.Value));
}
