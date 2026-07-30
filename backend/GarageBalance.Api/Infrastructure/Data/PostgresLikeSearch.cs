namespace GarageBalance.Api.Infrastructure.Data;

internal static class PostgresLikeSearch
{
    public static string ContainsPattern(string value) =>
        $"%{value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)}%";
}
