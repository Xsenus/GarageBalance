using System.Text.Json;
using System.Text.Json.Serialization;

namespace GarageBalance.StorageTool;

public sealed record MaintenanceParticipantState(
    [property: JsonRequired] bool HealthTimerWasActive,
    [property: JsonRequired] bool StorageTimerWasActive,
    [property: JsonRequired] string[] ActiveStorageUnits)
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] Commands = ["inventory", "plan", "copy", "resume", "delta-sync", "verify", "cutover-check", "status", "checkpoint-check", "backup-layout", "backup-layout-prune"];
    public static IReadOnlyList<string> StorageUnits { get; } = Array.AsReadOnly(Commands.Select(command => $"garagebalance-storage-tool@{command}.service").ToArray());

    public static bool IsStorageUnit(string name) => Commands.Any(command => name == $"garagebalance-storage-tool@{command}.service");

    public void Validate()
    {
        if (ActiveStorageUnits is null || ActiveStorageUnits.Length > Commands.Length ||
            ActiveStorageUnits.Any(unit => !IsStorageUnit(unit)) ||
            !ActiveStorageUnits.SequenceEqual(ActiveStorageUnits.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("maintenance_participant_state_invalid");
    }

    public string Encode()
    {
        Validate();
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, Json));
    }

    public static MaintenanceParticipantState Decode(string encoded)
    {
        try
        {
            if (encoded.Length > 2048) throw new InvalidOperationException("maintenance_participant_state_invalid");
            var state = JsonSerializer.Deserialize<MaintenanceParticipantState>(Convert.FromBase64String(encoded), Json)
                ?? throw new InvalidOperationException("maintenance_participant_state_invalid");
            state.Validate();
            // A canonical encoding rejects duplicate/missing properties and partial writes.
            if (state.Encode() != encoded) throw new InvalidOperationException("maintenance_participant_state_invalid");
            return state;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            throw new InvalidOperationException("maintenance_participant_state_invalid");
        }
    }
}
