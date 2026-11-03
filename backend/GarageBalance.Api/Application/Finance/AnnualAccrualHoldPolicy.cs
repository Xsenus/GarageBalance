using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Finance;

public interface IAnnualAccrualHoldPolicy
{
    Task<bool> IsHeldAsync(int? accountingYear, CancellationToken cancellationToken);
}

// An explicit, reversible decision for one accounting year. Other years and all
// monthly services retain their configured behavior; the worker stays enabled.
public sealed class AnnualAccrualHoldPolicy(IApplicationSettingRepository settings) : IAnnualAccrualHoldPolicy
{
    private readonly Dictionary<int, bool> heldYears = [];
    public const string KeyPrefix = "finance.annual_accrual_hold.";
    public static string Key(int year) => year is >= 1900 and <= 9999
        ? KeyPrefix + year.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(year));

    public async Task<bool> IsHeldAsync(int? accountingYear, CancellationToken cancellationToken)
    {
        if (accountingYear is null or < 1900 or > 9999) return false;
        if (!heldYears.TryGetValue(accountingYear.Value, out var held))
        {
            held = (await settings.FindAsync(Key(accountingYear.Value), cancellationToken))?.BooleanValue == true;
            heldYears.Add(accountingYear.Value, held);
        }
        return held;
    }

    public static string Message(int year) => $"Годовые начисления за {year} год приостановлены по решению владельца. Месячные начисления продолжаются с установленного начала учёта гаража.";
}
