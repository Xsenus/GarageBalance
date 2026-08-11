namespace GarageBalance.Api.Domain.Finance;

public static class MeterKinds
{
    public const string Water = "water";
    public const string Electricity = "electricity";
    private const string ServicePrefix = "service_";

    public static string ForService(Guid serviceId) => $"{ServicePrefix}{serviceId:N}";

    public static bool IsValid(string? value)
    {
        if (value is Water or Electricity)
        {
            return true;
        }

        return value is { Length: 40 } &&
            value.StartsWith(ServicePrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(value[ServicePrefix.Length..], "N", out _);
    }

    public static Guid GetLockId(string meterKind)
    {
        if (meterKind.StartsWith(ServicePrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(meterKind[ServicePrefix.Length..], "N", out var serviceId))
        {
            return serviceId;
        }

        throw new ArgumentOutOfRangeException(nameof(meterKind), meterKind, "Unsupported service meter kind.");
    }
}
