namespace GarageBalance.Api.Domain.Finance;

/// <summary>Consumption of every removed device in a month's replacement chain.</summary>
public static class MeterReplacementSegments
{
    public static decimal? CalculateCarry(
        MeterReading reading, MeterReading? previousReading, IReadOnlyList<MeterDevice> devices)
    {
        if (!reading.IsMeterReplacement || reading.MeterDevice is null)
        {
            return reading.PreviousDeviceConsumption;
        }

        var carry = 0m;
        var successor = reading.MeterDevice;
        var visited = new HashSet<Guid> { successor.Id };
        while (!MeterBaselineResolver.IsRestartBoundary(successor) && successor.InstalledOn.Year == reading.AccountingMonth.Year && successor.InstalledOn.Month == reading.AccountingMonth.Month)
        {
            var predecessor = FindPredecessor(successor, devices);
            if (predecessor is null)
            {
                // Old records may predate registration of a physical device.
                return carry == 0m ? reading.PreviousDeviceConsumption : carry;
            }

            if (!visited.Add(predecessor.Id) || !predecessor.FinalValue.HasValue)
            {
                return null;
            }

            var start = previousReading?.MeterDeviceId == predecessor.Id
                ? previousReading.CurrentValue : predecessor.InitialValue;
            var segment = Math.Round(predecessor.FinalValue.Value - start, 3, MidpointRounding.AwayFromZero);
            if (segment < 0m)
            {
                return null;
            }

            carry = Math.Round(carry + segment, 3, MidpointRounding.AwayFromZero);
            successor = predecessor;
        }

        return carry;
    }

    public static MeterDevice? FindPredecessor(MeterDevice successor, IReadOnlyList<MeterDevice> devices) =>
        successor.ReplacedDeviceId.HasValue
            ? devices.FirstOrDefault(device => !device.IsCanceled && device.Id == successor.ReplacedDeviceId.Value)
            : devices.Where(device => !device.IsCanceled && device.Id != successor.Id &&
                    device.InstalledOn < successor.InstalledOn && device.RemovedOn == successor.InstalledOn.AddDays(-1))
                .OrderByDescending(device => device.InstalledOn).ThenByDescending(device => device.Id).FirstOrDefault();
}
