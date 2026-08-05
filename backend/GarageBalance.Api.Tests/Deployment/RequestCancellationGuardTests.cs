using System.Reflection;
using System.Text.RegularExpressions;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class RequestCancellationGuardTests
{
    private static readonly string[] DatabaseAsyncOperators =
    [
        "AnyAsync",
        "CountAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "LongCountAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "SumAsync",
        "ToArrayAsync",
        "ToDictionaryAsync",
        "ToHashSetAsync",
        "ToListAsync"
    ];

    [Fact]
    public void AsyncHttpActions_AcceptRequestCancellationToken()
    {
        var violations = typeof(ReportsController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Where(method => method.GetParameters().All(parameter => parameter.ParameterType != typeof(CancellationToken)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Async HTTP actions without CancellationToken:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void HeavyDatabaseReads_DoNotUseUncancellableEfAsyncOperators()
    {
        var dataDirectory = RepositoryPathLocator.FindApiFile("Infrastructure/Data/GarageBalanceDbContext.cs").Directory!;
        var guardedFiles = dataDirectory
            .EnumerateFiles("*.cs", SearchOption.TopDirectoryOnly)
            .Where(file =>
                file.Name.EndsWith("ReportQuery.cs", StringComparison.Ordinal)
                || file.Name is "EfAuditEventRepository.cs"
                    or "EfImportRepository.cs"
                    or "EfImportQuarantineRepository.cs")
            .ToArray();
        var parameterlessOperator = new Regex(
            $@"\.({string.Join("|", DatabaseAsyncOperators.Select(Regex.Escape))})\s*\(\s*\)",
            RegexOptions.CultureInvariant);
        var violations = guardedFiles
            .SelectMany(file =>
            {
                var source = File.ReadAllText(file.FullName);
                return parameterlessOperator.Matches(source)
                    .Select(match => $"{file.Name}: {match.Value}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(guardedFiles);
        Assert.True(
            violations.Length == 0,
            $"Heavy database reads without request cancellation:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Theory]
    [InlineData("Controllers/ReportsController.cs", "reportService")]
    [InlineData("Controllers/AuditController.cs", "auditService")]
    [InlineData("Controllers/ImportController.cs", "importService")]
    public void ExportControllers_ForwardCancellationToken(string relativePath, string serviceName)
    {
        var source = File.ReadAllText(RepositoryPathLocator.FindApiFile(relativePath).FullName);
        var awaitedCalls = Regex.Matches(
            source,
            $@"await\s+{Regex.Escape(serviceName)}\.\w*Export\w*Async\s*\(",
            RegexOptions.CultureInvariant);
        var forwardedCalls = Regex.Matches(
            source,
            $@"await\s+{Regex.Escape(serviceName)}\.\w*Export\w*Async\s*\([\s\S]*?cancellationToken\s*\)\s*;",
            RegexOptions.CultureInvariant);

        Assert.NotEmpty(awaitedCalls);
        Assert.Equal(awaitedCalls.Count, forwardedCalls.Count);
        Assert.DoesNotContain("CancellationToken.None", source, StringComparison.Ordinal);
    }
}
