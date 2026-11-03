namespace GarageBalance.StorageTool;

// The journal distinguishes a prepared operation from one whose COMMIT outcome
// may be unknown. An executing operation is never eligible for DELETE retry.
public sealed record MaintenancePreparationProgress(Guid PlanId, string Phase, string? Fingerprint)
{
    public static MaintenancePreparationProgress Parse(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length != 4 || lines[3] != "" || !Guid.TryParseExact(lines[0], "D", out var id) || id == Guid.Empty ||
            lines[1] is not ("preparing" or "prepared" or "executing") ||
            (lines[1] == "preparing" ? lines[2] != "" : lines[2].Length != 64 || !lines[2].All(Uri.IsHexDigit)))
            throw new InvalidOperationException("maintenance_progress_invalid");
        return new(id, lines[1], lines[2].Length == 0 ? null : lines[2]);
    }

    public string Encode()
    {
        var value = $"{PlanId:D}\n{Phase}\n{Fingerprint}\n";
        if (Parse(value) != this) throw new InvalidOperationException("maintenance_progress_invalid");
        return value;
    }

    public static void ValidateTransition(MaintenancePreparationProgress? previous, MaintenancePreparationProgress next)
    {
        _ = next.Encode();
        if (previous is null)
        {
            if (next.Phase != "preparing") throw new InvalidOperationException("maintenance_progress_transition_invalid");
            return;
        }
        _ = previous.Encode();
        if (previous.PlanId != next.PlanId ||
            !(previous.Phase == "preparing" && next.Phase == "prepared" ||
              previous.Phase == "prepared" && next.Phase == "executing" && previous.Fingerprint == next.Fingerprint))
            throw new InvalidOperationException("maintenance_progress_transition_invalid");
    }
}
