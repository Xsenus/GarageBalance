using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace GarageBalance.Api.Tests.Releases;

public sealed class PostgreSqlAppReleasePersistenceIntegrationTests
{
    [PostgreSqlFact]
    public async Task UnitOfWork_MapsConcurrentReleaseVersionToPersistenceConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var version = $"concurrent-{Guid.NewGuid():N}";
        var publishedAt = DateTimeOffset.Parse("2026-08-04T10:00:00+07:00");
        var first = new AppReleaseDto("concurrent-1", version, publishedAt, "Первая запись", "Описание.", [new AppReleaseItemDto("fixed", "Пункт.")], true);
        var second = first with { ReleaseId = "concurrent-2", Title = "Вторая запись" };
        await new EfAppReleaseRepository(firstContext).StageUpsertAsync(first, CancellationToken.None);
        await new EfAppReleaseRepository(secondContext).StageUpsertAsync(second, CancellationToken.None);

        await new EfApplicationUnitOfWork(firstContext).SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ApplicationPersistenceConflictException>(() =>
            new EfApplicationUnitOfWork(secondContext).SaveChangesAsync(CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task DatabaseCatalog_IsAtomicAndRemainsAuthoritativeOverReadOnlySource()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"garagebalance-release-pg-{Guid.NewGuid():N}");
        var releasesDirectory = Path.Combine(rootPath, "AppReleases");
        var sourcePath = Path.Combine(releasesDirectory, "releases.json");
        Directory.CreateDirectory(releasesDirectory);
        await File.WriteAllTextAsync(sourcePath, "[]");
        File.SetAttributes(sourcePath, FileAttributes.ReadOnly);

        try
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var context = database.CreateContext();
            var repository = new EfAppReleaseRepository(context);
            var service = new AppReleaseService(
                new FakeWebHostEnvironment(rootPath),
                new EfApplicationUnitOfWork(context),
                new AuditEventWriter(context),
                repository);
            var actorUserId = Guid.NewGuid();
            var initialAuditCount = await context.AuditEvents.CountAsync();
            var request = new UpsertAppReleaseRequest(
                "postgres-release",
                "0.930.1",
                DateTimeOffset.Parse("2026-08-04T10:00:00+07:00"),
                "Запись администратора",
                "Проверка надёжного каталога.",
                [new AppReleaseItemDto("fixed", "Изменение сохранено в PostgreSQL.")],
                false);

            var created = await service.CreateReleaseAsync(request, actorUserId, CancellationToken.None);
            var published = await service.PublishReleaseAsync("postgres-release", actorUserId, CancellationToken.None);

            Assert.True(created.Succeeded);
            Assert.True(published.Succeeded);
            Assert.Equal("[]", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal(initialAuditCount + 2, await context.AuditEvents.CountAsync());
            await repository.SynchronizeAsync(
                [new AppReleaseDto(
                    "postgres-release",
                    "0.930.0",
                    DateTimeOffset.Parse("2026-08-04T09:00:00+07:00"),
                    "Устаревший исходный текст",
                    "Не должен затереть запись администратора.",
                    [new AppReleaseItemDto("fixed", "Исходный пункт.")],
                    true)],
                CancellationToken.None);

            var stored = Assert.Single(await context.AppReleases.AsNoTracking().ToArrayAsync());
            Assert.Equal("0.930.1", stored.Version);
            Assert.Equal("Запись администратора", stored.Title);
            Assert.True(stored.IsPublished);
        }
        finally
        {
            File.SetAttributes(sourcePath, FileAttributes.Normal);
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class FakeWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GarageBalance.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
