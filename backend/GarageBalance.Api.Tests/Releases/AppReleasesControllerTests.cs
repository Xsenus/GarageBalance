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

    [Fact]
    public async Task CreateRelease_ReturnsCreatedForManageRequest()
    {
        var created = new AppReleaseDto(
            "release-2",
            "0.2.0",
            DateTimeOffset.Parse("2026-06-24T10:00:00+07:00"),
            "Новая запись",
            "Администратор подготовил запись.",
            [new AppReleaseItemDto("new", "Пункт.")],
            false);
        var controller = new AppReleasesController(new FakeAppReleaseService
        {
            CreateResult = AppReleaseResult<AppReleaseDto>.Success(created)
        });

        var result = await controller.CreateRelease(
            new UpsertAppReleaseRequest("release-2", "0.2.0", created.PublishedAt, "Новая запись", "Администратор подготовил запись.", created.Items),
            CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(AppReleasesController.GetReleases), createdResult.ActionName);
        Assert.Same(created, createdResult.Value);
    }

    [Fact]
    public async Task UpdateRelease_ReturnsNotFoundForMissingRelease()
    {
        var controller = new AppReleasesController(new FakeAppReleaseService
        {
            UpdateResult = AppReleaseResult<AppReleaseDto>.Failure("release_not_found", "Запись истории обновлений не найдена.")
        });

        var result = await controller.UpdateRelease(
            "missing",
            new UpsertAppReleaseRequest(null, "0.2.0", null, "Заголовок", "Описание", [new AppReleaseItemDto("fixed", "Исправление.")]),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("release_not_found", problem.Title);
    }

    private sealed class FakeAppReleaseService : IAppReleaseService
    {
        public AppReleaseResult<IReadOnlyList<AppReleaseDto>> Result { get; init; } =
            AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure("not_configured", "Not configured.");

        public AppReleaseResult<IReadOnlyList<AppReleaseDto>> ManageResult { get; init; } =
            AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure("not_configured", "Not configured.");

        public AppReleaseResult<AppReleaseDto> CreateResult { get; init; } =
            AppReleaseResult<AppReleaseDto>.Failure("not_configured", "Not configured.");

        public AppReleaseResult<AppReleaseDto> UpdateResult { get; init; } =
            AppReleaseResult<AppReleaseDto>.Failure("not_configured", "Not configured.");

        public AppReleaseResult<AppReleaseDto> PublishResult { get; init; } =
            AppReleaseResult<AppReleaseDto>.Failure("not_configured", "Not configured.");

        public Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetReleasesAsync(int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result);
        }

        public Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetManageableReleasesAsync(int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(ManageResult);
        }

        public Task<AppReleaseResult<AppReleaseDto>> CreateReleaseAsync(UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResult);
        }

        public Task<AppReleaseResult<AppReleaseDto>> UpdateReleaseAsync(string releaseId, UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateResult);
        }

        public Task<AppReleaseResult<AppReleaseDto>> PublishReleaseAsync(string releaseId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublishResult);
        }
    }
}
