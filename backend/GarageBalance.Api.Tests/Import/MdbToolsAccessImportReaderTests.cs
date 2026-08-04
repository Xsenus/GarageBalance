using GarageBalance.Api.Infrastructure.Import;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Import;

public sealed class MdbToolsAccessImportReaderTests
{
    [Fact]
    public async Task GetStatusAsync_ReturnsReadyWhenMdbToolsResponds()
    {
        var runner = new FakeRunner(new AccessImportCommandResult(true, 0, "mdbtools 1.0", string.Empty));
        var reader = CreateReader(runner);

        var result = await reader.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal("ready", result.Status);
        Assert.Equal(["--version"], runner.LastArguments);
    }

    [Fact]
    public async Task GetStatusAsync_DoesNotExposeProcessDetailsWhenExecutableIsMissing()
    {
        var reader = CreateReader(new FakeRunner(
            new AccessImportCommandResult(false, -1, string.Empty, "reader_not_installed")));

        var result = await reader.GetStatusAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal("reader_not_installed", result.Status);
        Assert.DoesNotContain("C:\\", result.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_ReadsUserTablesFiltersSystemTablesAndRemovesPrivateCopy()
    {
        var runner = new FakeRunner(
            new AccessImportCommandResult(true, 0, "MSysObjects\r\nГаражи\r\nВладельцы\r\nгаражи\r\n", string.Empty));
        var reader = CreateReader(runner);

        var result = await reader.InspectAsync(new byte[] { 1, 2, 3, 4 }, ".mdb", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["Владельцы", "Гаражи"], result.TableNames);
        Assert.Equal("-1", runner.LastArguments[0]);
        Assert.EndsWith("source.mdb", runner.LastArguments[1], StringComparison.Ordinal);
        Assert.False(File.Exists(runner.LastArguments[1]));
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.LastArguments[1])));
    }

    [Fact]
    public async Task InspectAsync_RejectsUnexpectedlyLargeSchema()
    {
        var runner = new FakeRunner(
            new AccessImportCommandResult(true, 0, "one\ntwo\nthree\n", string.Empty));
        var reader = CreateReader(runner, maximumTableCount: 2);

        var result = await reader.InspectAsync(new byte[] { 1 }, ".accdb", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("reader_table_limit_exceeded", result.Status);
    }

    private static MdbToolsAccessImportReader CreateReader(FakeRunner runner, int maximumTableCount = 250) =>
        new(
            Options.Create(new MdbToolsAccessImportReaderOptions
            {
                Enabled = true,
                ExecutablePath = "mdb-tables-test",
                TimeoutSeconds = 5,
                MaximumTableCount = maximumTableCount
            }),
            runner);

    private sealed class FakeRunner(AccessImportCommandResult result) : IAccessImportCommandRunner
    {
        public IReadOnlyList<string> LastArguments { get; private set; } = [];

        public Task<AccessImportCommandResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastArguments = arguments.ToArray();
            return Task.FromResult(result);
        }
    }
}
