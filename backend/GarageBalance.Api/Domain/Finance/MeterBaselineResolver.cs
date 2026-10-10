namespace GarageBalance.Api.Domain.Finance;

/// <summary>
/// Finds the meter device whose initial value is the garage "start value" for one meter kind.
/// </summary>
public static class MeterBaselineResolver
{
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
        return firstActiveReading.IsMeterReplacement && anchorIndex > 0
            ? devicesAscending[anchorIndex - 1]
            : devicesAscending[anchorIndex];
    }

    public static bool IsRestartBoundary(MeterDevice device) =>
        device.SerialNumber.StartsWith(RestartDeviceSerialPrefix, StringComparison.Ordinal);
}
