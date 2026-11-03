using System.Globalization;
using System.Text.Json.Nodes;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

internal static class AnnualAccrualGuardEpoch
{
    public const string Key = "finance.annual_accrual_guard_epoch";

    public static async Task<ApplicationSetting> ReadAndValidateAsync(GarageBalanceDbContext db, bool tracked, CancellationToken token)
    {
        var query = tracked ? db.ApplicationSettings.AsQueryable() : db.ApplicationSettings.AsNoTracking();
        var epoch = await query.SingleOrDefaultAsync(item => item.Key == Key, token) ?? throw Invalid();
        var map = Parse(epoch.JsonValue);
        var active = await db.ApplicationSettings.AsNoTracking()
            .Where(item => item.BooleanValue && item.Key.StartsWith(AnnualAccrualHoldPolicy.KeyPrefix))
            .Select(item => item.Key).ToListAsync(token);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            var year = item[AnnualAccrualHoldPolicy.KeyPrefix.Length..];
            if (year.Length != 4 || !int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1900)
                throw Invalid();
            expected.Add(year);
        }
        if (!expected.SetEquals(map.Select(item => item.Key))) throw Invalid();
        return epoch;
    }

    public static void SetYear(ApplicationSetting epoch, int year, bool held, DateTimeOffset now)
    {
        var map = Parse(epoch.JsonValue);
        var key = year.ToString(CultureInfo.InvariantCulture);
        if (held) map[key] = true;
        else if (!map.Remove(key)) throw Invalid();
        epoch.JsonValue = map.ToJsonString();
        epoch.Version = Guid.NewGuid();
        epoch.UpdatedAtUtc = now;
    }

    private static JsonObject Parse(string? value)
    {
        JsonObject? map;
        try { map = JsonNode.Parse(value ?? "null") as JsonObject; }
        catch (System.Text.Json.JsonException) { throw Invalid(); }
        if (map is null) throw Invalid();
        foreach (var item in map)
            if (item.Key.Length != 4 || !int.TryParse(item.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 1900 ||
                item.Value is not JsonValue scalar || !scalar.TryGetValue<bool>(out var held) || !held) throw Invalid();
        return map;
    }

    private static AccountingCleanupException Invalid() => new("cleanup_annual_guard_invalid", "Состояние защитной строки годовых начислений не совпадает с политиками.");
}
