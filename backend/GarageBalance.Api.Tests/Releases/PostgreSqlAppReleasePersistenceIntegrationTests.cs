using System.Data.Common;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Domain.Releases;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Npgsql;

namespace GarageBalance.Api.Tests.Releases;

public sealed class PostgreSqlAppReleasePersistenceIntegrationTests
{
    [PostgreSqlFact]
    public async Task ReleasePage_ReturnsRowsAndExactTotalInOneBoundedCommand()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AppReleases.AddRange(
                new AppReleaseRecord
                {
                    ReleaseId = "release-old",
                    Version = "1.1.0",
                    PublishedAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
                    Title = "Старый релиз",
                    Summary = "Старое описание.",
                    ItemsJson = "[{\"type\":\"fixed\",\"text\":\"Старое исправление.\"}]",
                    IsPublished = true
                },
                new AppReleaseRecord
                {
                    ReleaseId = "release-new",
                    Version = "1.2.0",
                    PublishedAt = DateTimeOffset.Parse("2026-08-02T10:00:00Z"),
                    Title = "Новый релиз",
                    Summary = "Новое описание.",
                    ItemsJson = "[{\"type\":\"improved\",\"text\":\"Новое улучшение.\"}]",
                    IsPublished = true
                },
                new AppReleaseRecord
                {
                    ReleaseId = "release-draft",
                    Version = "1.3.0",
                    PublishedAt = DateTimeOffset.Parse("2026-08-03T10:00:00Z"),
                    Title = "Черновик",
                    Summary = "Описание черновика.",
                    ItemsJson = "[]",
                    IsPublished = false
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfAppReleaseRepository(context);

        var publicPage = await repository.GetPageAsync(false, 0, 1, CancellationToken.None);

        Assert.Equal(2, publicPage.TotalCount);
        Assert.True(publicPage.HasMore);
        var published = Assert.Single(publicPage.Items);
        Assert.Equal("release-new", published.ReleaseId);
        Assert.Equal("Новое улучшение.", Assert.Single(published.Items).Text);
        AssertSingleCombinedPageCommand(capture.Commands);
        capture.Commands.Clear();

        var emptyPage = await repository.GetPageAsync(true, 10, 5, CancellationToken.None);

        Assert.Equal(3, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Items);
        Assert.False(emptyPage.HasMore);
        AssertSingleCombinedPageCommand(capture.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task UnitOfWork_MapsConcurrentReleaseVersionToPersistenceConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var version = $"concurrent-{Guid.NewGuid():N}";
        var publishedAt = DateTimeOffset.Parse("2026-08-04T10:00:00+07:00");
        var first = new AppReleaseDto("concurrent-1", version, publishedAt, "Первая запись", "Описание.", [new AppReleaseItemDto("fixed", "Пункт.")], true);
        var second = first with
        {
            ReleaseId = "concurrent-2",
            Version = version.ToUpperInvariant(),
            Title = "Вторая запись"
        };
        await new EfAppReleaseRepository(firstContext).StageUpsertAsync(first, CancellationToken.None);
        await new EfAppReleaseRepository(secondContext).StageUpsertAsync(second, CancellationToken.None);

        await new EfApplicationUnitOfWork(firstContext).SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ApplicationPersistenceConflictException>(() =>
            new EfApplicationUnitOfWork(secondContext).SaveChangesAsync(CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task VersionLookup_UsesCaseInsensitiveUniqueIndexAndRejectsCaseVariant()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using (var seedCommand = connection.CreateCommand())
        {
            seedCommand.CommandText =
                """
                INSERT INTO app_releases
                    ("ReleaseId", "Version", "PublishedAt", "Title", "Summary", "ItemsJson", "IsPublished")
                SELECT 'plan-' || i,
                       'version-' || i,
                       TIMESTAMPTZ '2026-01-01' + i * INTERVAL '1 minute',
                       'Release ' || i,
                       'Summary',
                       '[]'::jsonb,
                       true
                FROM generate_series(1, 500) i;

                ANALYZE app_releases;
                """;
            await seedCommand.ExecuteNonQueryAsync();
        }

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                """
                SELECT indexdef
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'app_releases'
                  AND indexname = 'IX_app_releases_Version_ci';
                """;
            var indexDefinition = Assert.IsType<string>(await indexCommand.ExecuteScalarAsync());
            Assert.Contains("CREATE UNIQUE INDEX", indexDefinition, StringComparison.Ordinal);
            Assert.Contains("lower((\"Version\")::text)", indexDefinition, StringComparison.OrdinalIgnoreCase);
        }

        await using (var explainCommand = connection.CreateCommand())
        {
            explainCommand.CommandText =
                """
                SET enable_seqscan = off;
                EXPLAIN (FORMAT TEXT)
                SELECT "ReleaseId"
                FROM app_releases
                WHERE LOWER("Version") = 'version-349'
                LIMIT 1;
                """;
            var planLines = new List<string>();
            await using var reader = await explainCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                planLines.Add(reader.GetString(0));
            }

            Assert.Contains(
                "IX_app_releases_Version_ci",
                string.Join(Environment.NewLine, planLines),
                StringComparison.Ordinal);
        }

        await using var duplicateCommand = connection.CreateCommand();
        duplicateCommand.CommandText =
            """
            INSERT INTO app_releases
                ("ReleaseId", "Version", "PublishedAt", "Title", "Summary", "ItemsJson", "IsPublished")
            VALUES
                ('case-variant', 'VERSION-349', TIMESTAMPTZ '2026-08-30', 'Duplicate', 'Summary', '[]'::jsonb, true);
            """;
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => duplicateCommand.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("IX_app_releases_Version_ci", duplicate.ConstraintName);
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

    private static void AssertSingleCombinedPageCommand(IReadOnlyCollection<string> commands)
    {
        var command = Assert.Single(commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
