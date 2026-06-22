using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Releases;

public sealed class AppReleasesControllerTests
{
    [Fact]
    public async Task GetReleases_ReturnsOk()
    {
        var releases = new[]
        {
            new AppReleaseDto(
                "release-1",
                "0.1.0",
                DateTimeOffset.Parse("2026-06-23T10:00:00+07:00"),
                "История обновлений",
                "Пользователь видит изменения версии.",
                [new AppReleaseItemDto("new", "Добавлен журнал.")])
        };
        var controller = new AppReleasesController(new FakeAppReleaseService
        {
            Result = AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Success(releases)
        });

        var result = await controller.GetReleases(10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(releases, ok.Value);
    }

    [Fact]
    public async Task GetReleases_ReturnsServerErrorForServiceFailure()
    {
        var controller = new AppReleasesController(new FakeAppReleaseService
        {
            Result = AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure("releases_file_invalid", "Файл истории обновлений содержит некорректный JSON.")
        });

        var result = await controller.GetReleases(null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("releases_file_invalid", problem.Title);
    }

    private sealed class FakeAppReleaseService : IAppReleaseService
    {
        public AppReleaseResult<IReadOnlyList<AppReleaseDto>> Result { get; init; } =
            AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure("not_configured", "Not configured.");

        public Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetReleasesAsync(int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result);
        }
    }
}
