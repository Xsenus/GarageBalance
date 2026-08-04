using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class LegacyFormStateRemovalTests
{
    [Fact]
    public void RuntimeModelAndHttpSurface_DoNotExposeLegacySharedFormState()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new GarageBalanceDbContext(options);

        Assert.DoesNotContain(
            context.Model.GetEntityTypes(),
            entity => string.Equals(entity.GetTableName(), "form_states", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(GarageBalance.Api.Controllers.AppReleasesController).Assembly.GetTypes(),
            type => string.Equals(type.Name, "FormStatesController", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentMigration_DropsLegacyTable_AndFrontendHasNoSharedStateClient()
    {
        var root = FindRepositoryRoot();
        var migrationsDirectory = Path.Combine(root, "backend", "GarageBalance.Api", "Infrastructure", "Data", "Migrations");
        var migration = Directory.GetFiles(migrationsDirectory, "*_RemoveLegacyFormStates.cs").Single();
        var migrationSource = File.ReadAllText(migration);

        Assert.Contains("DropTable", migrationSource, StringComparison.Ordinal);
        Assert.Contains("form_states", migrationSource, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "frontend", "src", "services", "formStatesApi.ts")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
