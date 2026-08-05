namespace GarageBalance.Api.Tests.Common;

internal static class RepositoryPathLocator
{
    public static FileInfo FindApiFile(string relativePath)
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                foreach (var candidatePath in new[]
                         {
                             Path.Combine(directory.FullName, "backend", "GarageBalance.Api", relativePath),
                             Path.Combine(directory.FullName, "GarageBalance.Api", relativePath)
                         })
                {
                    var candidate = new FileInfo(candidatePath);
                    if (candidate.Exists)
                    {
                        return candidate;
                    }
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate GarageBalance.Api/{relativePath}.");
    }
}
