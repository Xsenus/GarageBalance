namespace GarageBalance.Api.Domain.Finance;

/// <summary>
/// Finds the meter device whose initial value is the garage "start value" for one meter kind.
/// </summary>
public static class MeterBaselineResolver
{
    // A logical restart begins a new accounting chain. Retain earlier devices in
    // history, but do not let a chain without active readings override its opening.
    // If readings still belong to an earlier device (or an explicit replacement),
    // FindBaselineDevice keeps that earlier chain as the accounting baseline.
    public static bool IsAccountingOpening(MeterDevice device, IReadOnlyList<MeterDevice> history, MeterReading? firstActiveReading,
        DateOnly? accountingStartMonth = null, DateOnly? initialMeterReadingMonth = null)
    {
        if (device.IsCanceled) return false;
        var baseline = FindBaselineDevice(history.OrderBy(item => item.InstalledOn).ThenBy(item => item.Id).ToArray(), firstActiveReading);
        if (baseline is not null && baseline.Id != device.Id) return false;
        return IsGeneratedOpening(device, history) || IsRestartBoundary(device) &&
            !device.ReplacedDeviceId.HasValue && !device.ReplacementAccountingMonth.HasValue &&
            accountingStartMonth.HasValue && initialMeterReadingMonth == accountingStartMonth.Value.AddMonths(-1) &&
            baseline?.Id == device.Id;
    }

    public static DateOnly GetAccountingStartDate(MeterDevice device, DateOnly? accountingStartMonth,
        IReadOnlyList<MeterDevice> history, MeterReading? firstActiveReading, DateOnly? initialMeterReadingMonth = null) =>
        IsAccountingOpening(device, history, firstActiveReading, accountingStartMonth, initialMeterReadingMonth)
            ? GetEffectiveStartDate(device, accountingStartMonth) : device.InstalledOn;

    // Generated opening devices carry an accounting boundary, not the installation
    // date of a physical replacement. Explicit connections are resolved by callers.
    public static bool IsGeneratedOpening(MeterDevice device, IReadOnlyList<MeterDevice>? history = null) =>
        !device.ReplacedDeviceId.HasValue && !device.ReplacementAccountingMonth.HasValue &&
        (history is null || !history.Any(previous => previous.Id != device.Id && previous.InstalledOn < device.InstalledOn)) &&
        (IsRestartBoundary(device) || device.SerialNumber == UnnumberedDeviceSerial);

    public static DateOnly GetEffectiveStartDate(MeterDevice device, DateOnly? accountingStartMonth, IReadOnlyList<MeterDevice>? history = null) =>
        accountingStartMonth.HasValue && IsGeneratedOpening(device, history) &&
        new DateOnly(device.InstalledOn.Year, device.InstalledOn.Month, 1) > new DateOnly(accountingStartMonth.Value.Year, accountingStartMonth.Value.Month, 1)
            ? new DateOnly(accountingStartMonth.Value.Year, accountingStartMonth.Value.Month, 1)
            : device.InstalledOn;

    /// <summary>Serial number prefix of the logical device created when meter accounting was restarted.</summary>
    public const string RestartDeviceSerialPrefix = "Начало учёта";

    /// <summary>Serial number of a device registered automatically when no number was known.</summary>
    public const string UnnumberedDeviceSerial = "Без номера";

    /// <param name="devicesAscending">Devices of one garage and meter kind ordered by installation.</param>
    /// <param name="firstActiveReading">Earliest non-canceled reading of the same garage and kind, if any.</param>
    public static MeterDevice? FindBaselineDevice(IReadOnlyList<MeterDevice> devicesAscending, MeterReading? firstActiveReading)
    {
        if (devicesAscending.Count == 0)
        {
            return null;
        }

        if (firstActiveReading is null)
        {
            for (var index = devicesAscending.Count - 1; index >= 0; index--)
            {
                if (IsRestartBoundary(devicesAscending[index]))
                {
                    return devicesAscending[index];
                }
            }

            return devicesAscending[0];
        }

        var anchorIndex = -1;
        for (var index = 0; index < devicesAscending.Count; index++)
        {
            if (devicesAscending[index].Id == firstActiveReading.MeterDeviceId)
            {
                anchorIndex = index;
                break;
            }
        }

        if (anchorIndex < 0)
        {
            // Reading without a device is a legacy record: its start value lives on the garage.
            return null;
        }

        // A replacement reading consumes the removed device up to its final value, so the removed
        // device's initial value is part of the first reading's consumption.
        if (!firstActiveReading.IsMeterReplacement || anchorIndex == 0)
        {
            return devicesAscending[anchorIndex];
        }

        var baseline = devicesAscending[anchorIndex];
        var visited = new HashSet<Guid> { baseline.Id };
        do
        {
            var predecessor = MeterReplacementSegments.FindPredecessor(baseline, devicesAscending);
            // Preserve compatibility with old records without an explicit predecessor/date boundary.
            predecessor ??= devicesAscending.Take(anchorIndex).LastOrDefault(device => !device.IsCanceled);
            if (predecessor is null || !visited.Add(predecessor.Id)) break;
            baseline = predecessor;
            anchorIndex = devicesAscending.ToList().IndexOf(baseline);
        }
        while (anchorIndex > 0 && !IsRestartBoundary(baseline) && baseline.InstalledOn.Year == firstActiveReading.AccountingMonth.Year &&
            baseline.InstalledOn.Month == firstActiveReading.AccountingMonth.Month);

        return baseline;
    }

    public static bool IsRestartBoundary(MeterDevice device) =>
        device.SerialNumber.StartsWith(RestartDeviceSerialPrefix, StringComparison.Ordinal);
}
