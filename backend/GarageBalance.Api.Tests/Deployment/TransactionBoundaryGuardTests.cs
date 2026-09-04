namespace GarageBalance.Api.Tests.Deployment;

public sealed class TransactionBoundaryGuardTests
{
    private static readonly string[] ApprovedExplicitTransactions =
    [
        $"Infrastructure{Path.DirectorySeparatorChar}Maintenance{Path.DirectorySeparatorChar}WorkingDataResetExecutor.cs: BeginTransactionAsync("
    ];

    private static readonly string[] ExplicitTransactionMarkers =
    [
        "BeginTransaction(",
        "BeginTransactionAsync(",
        "TransactionScope(",
        "UseTransaction(",
        "UseTransactionAsync("
    ];

    [Fact]
    public void ProductionCode_DoesNotOpenLongLivedExplicitDatabaseTransactions()
    {
        var backendRoot = Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api");
        var violations = Directory
            .EnumerateFiles(backendRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => ExplicitTransactionMarkers
                .Where(marker => File.ReadAllText(path).Contains(marker, StringComparison.Ordinal))
                .Select(marker => $"{Path.GetRelativePath(backendRoot, path)}: {marker}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ApprovedExplicitTransactions.OrderBy(value => value, StringComparer.Ordinal), violations);
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
