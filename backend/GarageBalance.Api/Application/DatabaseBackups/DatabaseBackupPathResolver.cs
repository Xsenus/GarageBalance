namespace GarageBalance.Api.Application.Backups;

public static class DatabaseBackupPathResolver
{
    public static string Resolve(string configuredDirectory)
    {
        if (!string.Equals(configuredDirectory, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = AppContext.BaseDirectory;
        }

        return Path.Combine(localData, "GarageBalance", "backups");
    }
}
