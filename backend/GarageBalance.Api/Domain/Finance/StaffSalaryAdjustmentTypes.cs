namespace GarageBalance.Api.Domain.Finance;

public static class StaffSalaryAdjustmentTypes
{
    public const string Bonus = "bonus";
    public const string Penalty = "penalty";

    public static bool IsSupported(string? value) =>
        value is Bonus or Penalty;
}
