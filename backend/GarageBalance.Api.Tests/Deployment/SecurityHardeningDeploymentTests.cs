namespace GarageBalance.Api.Tests.Deployment;

public sealed class SecurityHardeningDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ForwardedClientAddressIsProcessedBeforeHttpsAndRateLimiting()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto", program, StringComparison.Ordinal);
        Assert.Contains("options.ForwardLimit = 1;", program, StringComparison.Ordinal);
        Assert.Contains("System.Net.IPNetwork.Parse(\"10.0.0.0/8\")", program, StringComparison.Ordinal);
        Assert.Contains("System.Net.IPNetwork.Parse(\"172.16.0.0/12\")", program, StringComparison.Ordinal);
        Assert.Contains("System.Net.IPNetwork.Parse(\"192.168.0.0/16\")", program, StringComparison.Ordinal);
        Assert.True(program.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal) < program.IndexOf("app.UseHttpsRedirection();", StringComparison.Ordinal));
        Assert.True(program.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal) < program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal));
        Assert.Contains("app.UseHsts();", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("frontend/nginx.conf", 5, false)]
    [InlineData("infrastructure/deployment/garagebalance-staging.nginx.conf", 5, true)]
    public void EveryNginxResponsePathHasProtectiveHeaders(string relativePath, int expectedLocations, bool expectsHsts)
    {
        var nginx = File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(expectedLocations, Count(nginx, "add_header X-Content-Type-Options \"nosniff\" always;"));
        Assert.Equal(expectedLocations, Count(nginx, "add_header X-Frame-Options \"DENY\" always;"));
        Assert.Equal(expectedLocations, Count(nginx, "add_header Referrer-Policy \"no-referrer\" always;"));
        Assert.Equal(expectedLocations, Count(nginx, "add_header Permissions-Policy"));
        Assert.Equal(expectedLocations, Count(nginx, "add_header Cross-Origin-Opener-Policy \"same-origin\" always;"));
        Assert.Equal(expectedLocations, Count(nginx, "add_header X-Permitted-Cross-Domain-Policies \"none\" always;"));
        Assert.Equal(expectedLocations, Count(nginx, "add_header Content-Security-Policy"));
        Assert.Equal(expectsHsts ? expectedLocations : 0, Count(nginx, "add_header Strict-Transport-Security"));
    }

    [Fact]
    public void ProductionShellUsesExternalBootstrapAssetsCompatibleWithStrictCsp()
    {
        var index = File.ReadAllText(Path.Combine(RepositoryRoot, "frontend", "index.html"));

        Assert.Contains("href=\"/bootstrap.css\"", index, StringComparison.Ordinal);
        Assert.Contains("src=\"/bootstrap.js\"", index, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DockerNginxKeepsHealthProxyValidAndImportLimitConsistent()
    {
        var nginx = File.ReadAllText(Path.Combine(RepositoryRoot, "frontend", "nginx.conf"));

        Assert.Contains("client_max_body_size 51m;", nginx, StringComparison.Ordinal);
        Assert.Contains("proxy_pass http://api:8080;", nginx, StringComparison.Ordinal);
        Assert.DoesNotContain("proxy_pass http://api:8080/health;", nginx, StringComparison.Ordinal);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
