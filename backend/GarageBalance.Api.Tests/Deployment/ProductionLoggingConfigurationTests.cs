using System.Text.Json;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class ProductionLoggingConfigurationTests
{
    [Fact]
    public void DefaultConfigurationDoesNotWriteEverySuccessfulDatabaseCommand()
    {
        var appSettingsPath = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "GarageBalance.Api",
            "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));

        var databaseCommandLevel = document.RootElement
            .GetProperty("Logging")
            .GetProperty("LogLevel")
            .GetProperty("Microsoft.EntityFrameworkCore.Database.Command")
            .GetString();

        Assert.Equal("Warning", databaseCommandLevel);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                 File.Exists(Path.Combine(directory.FullName, ".git"))))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
